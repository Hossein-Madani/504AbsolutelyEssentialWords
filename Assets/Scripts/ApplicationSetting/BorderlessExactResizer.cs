// BorderlessExactResizer.cs
// Windows-only: borderless popup window that auto-sizes to EXACT inner size (1280x800) on launch.
// Uses SetWindowPos (not Screen.SetResolution) to avoid OS/DPI rounding.
// Optionally keeps enforcing the exact size if the window changes.

using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using TMPro;

public class BorderlessExactResizer : MonoBehaviour
{
    [Header("UI (optional)")]
    public TextMeshProUGUI resolutionText;

    [Header("Target Inner Size")]
    public int targetInnerWidth = 1280;
    public int targetInnerHeight = 800;

    [Header("Behavior")]
    public bool autoFitOnStart = true;     // auto snap at startup
    public bool keepExactWhileRunning = false; // re-enforce if size changes

#if UNITY_STANDALONE_WIN
    // --- Win32 constants ---
    const int GWL_STYLE = -16;

    // Styles
    const uint WS_POPUP = 0x80000000;
    const uint WS_CAPTION = 0x00C00000; // (WS_BORDER | WS_DLGFRAME)
    const uint WS_THICKFRAME = 0x00040000;
    const uint WS_MINIMIZEBOX = 0x00020000;
    const uint WS_MAXIMIZEBOX = 0x00010000;
    const uint WS_SYSMENU = 0x00080000;

    // SetWindowPos flags
    const uint SWP_NOSIZE = 0x0001;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOZORDER = 0x0004;
    const uint SWP_FRAMECHANGED = 0x0020;

    const int WM_NCLBUTTONDOWN = 0x00A1;
    const int HTCAPTION = 0x02;

    // --- Win32 interop ---
    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    // 32/64-safe wrappers
    static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        if (IntPtr.Size == 8) return GetWindowLongPtr64(hWnd, nIndex);
        return new IntPtr(GetWindowLong32(hWnd, nIndex));
    }
    static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8) return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", EntryPoint = "GetWindowLong")] static extern int GetWindowLong32(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLong")] static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    IntPtr _hWnd = IntPtr.Zero;
    int _lastW, _lastH;
#endif

    void Start()
    {
        Screen.fullScreenMode = FullScreenMode.Windowed;

#if UNITY_STANDALONE_WIN
        StartCoroutine(Bootstrap());
#endif
    }

    IEnumerator Bootstrap()
    {
#if UNITY_STANDALONE_WIN
        yield return StartCoroutine(EnsureHandle());
        ApplyBorderless();

        if (autoFitOnStart)
            yield return StartCoroutine(FitToExactUsingSetWindowPos());

        if (keepExactWhileRunning)
            StartCoroutine(Watchdog());
#else
        yield break;
#endif
    }

    void Update()
    {
        if (resolutionText)
        {
            resolutionText.text =
                $"Window(inner): {Screen.width} x {Screen.height} | Desktop: {Display.main.systemWidth} x {Display.main.systemHeight}";
        }
    }

#if UNITY_STANDALONE_WIN
    // Re-enforce exact size if something else changes it
    IEnumerator Watchdog()
    {
        while (true)
        {
            if (Screen.width != _lastW || Screen.height != _lastH)
            {
                _lastW = Screen.width; _lastH = Screen.height;
                if (_lastW != targetInnerWidth || _lastH != targetInnerHeight)
                    yield return StartCoroutine(FitToExactUsingSetWindowPos());
            }
            yield return null;
        }
    }

    private IEnumerator FitToExactUsingSetWindowPos()
    {
        RefreshHandleAndReapplyBorderless();

        if (!GetWindowRect(_hWnd, out RECT r))
            yield break;
        int x = r.Left, y = r.Top;

        const int maxTries = 8;
        int desiredW = targetInnerWidth;
        int desiredH = targetInnerHeight;

        for (int i = 0; i < maxTries; i++)
        {
            SetWindowPos(_hWnd, IntPtr.Zero, x, y, desiredW, desiredH,
                SWP_NOZORDER | SWP_FRAMECHANGED);

            yield return null; // let Unity/DWM settle
            RefreshHandleAndReapplyBorderless();

            int errW = targetInnerWidth - Screen.width;
            int errH = targetInnerHeight - Screen.height;

            if (errW == 0 && errH == 0)
            {
                _lastW = Screen.width; _lastH = Screen.height;
                yield break;
            }

            desiredW += errW;
            desiredH += errH;
        }

        Debug.LogWarning($"Could not hit exactly {targetInnerWidth}x{targetInnerHeight}. " +
                         $"Ended at {Screen.width}x{Screen.height} after {maxTries} tries.");
    }

    IEnumerator EnsureHandle()
    {
        for (int i = 0; i < 120 && _hWnd == IntPtr.Zero; i++)
        {
            _hWnd = GetActiveWindow();
            if (_hWnd == IntPtr.Zero) _hWnd = GetForegroundWindow();
            yield return null;
        }
    }

    void OnApplicationFocus(bool focus)
    {
        if (focus) RefreshHandleAndReapplyBorderless();
    }

    void RefreshHandleAndReapplyBorderless()
    {
        var cur = GetActiveWindow();
        if (cur == IntPtr.Zero) cur = GetForegroundWindow();
        if (cur != IntPtr.Zero) _hWnd = cur;
        if (_hWnd != IntPtr.Zero) ApplyBorderless();
    }

    void ApplyBorderless()
    {
        uint style = (uint)(long)GetWindowLongPtr(_hWnd, GWL_STYLE);

        // Remove caption/border/maximize/resize
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MAXIMIZEBOX);
        // True borderless popup
        style |= WS_POPUP;
        // Keep sysmenu/minimize so it stays on taskbar and Alt+Tab works
        style |= WS_SYSMENU | WS_MINIMIZEBOX;

        SetWindowLongPtr(_hWnd, GWL_STYLE, (IntPtr)(long)style);

        // Recompute frame without moving/resizing
        SetWindowPos(_hWnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    // Optional: hook your custom title bar's PointerDown to allow dragging.
    public void BeginDrag()
    {
        if (_hWnd == IntPtr.Zero) return;
        ReleaseCapture();
        SendMessage(_hWnd, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }
#else
    public void BeginDrag() { } // no-op on non-Windows
#endif
}
