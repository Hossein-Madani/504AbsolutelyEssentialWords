using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class ApplicationFrame : MonoBehaviour
{
#if UNITY_STANDALONE_WIN
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();

    private const int SW_MINIMIZE = 6;
#endif

    /// <summary>
    /// Minimizes the Unity window just like the native Windows minimize button.
    /// </summary>
    public void MinimizeApp()
    {
#if UNITY_STANDALONE_WIN
        ShowWindow(GetActiveWindow(), SW_MINIMIZE);
#else
        Debug.Log("Native minimize is only supported on Windows in this method.");
#endif
    }

    /// <summary>
    /// Closes the application (quits).
    /// </summary>
    public void CloseApp()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
