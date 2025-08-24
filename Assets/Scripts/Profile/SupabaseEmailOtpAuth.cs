using System;
using System.Collections;
using System.Text;
using TMPro;                       // TextMeshPro
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;

public class SupabaseEmailOtpAuth : MonoBehaviour
{
    [Header("Supabase Project")]
    [Tooltip("Example: https://xyzcompany.supabase.co")]
    [SerializeField] private string projectUrl = "https://YOUR_REF.supabase.co";
    [Tooltip("Supabase anon public key (safe for clients when using RLS).")]
    [SerializeField] private string anonKey = "YOUR_PUBLIC_ANON_KEY";

    [Header("Login UI")]
    [SerializeField] private GameObject loginPanel;          // Show/hide this based on auth state
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private TMP_InputField codeInput;       // 6-digit code from email
    [SerializeField] private Button sendCodeButton;
    [SerializeField] private Button verifyButton;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Behavior")]
    [Tooltip("Try to refresh session at startup to auto-login the user.")]
    [SerializeField] private bool trySilentLoginOnStart = true;
    [Tooltip("Create new user automatically if email doesn't exist.")]
    [SerializeField] private bool createUserIfMissing = true;
    [Tooltip("Cooldown in seconds before allowing 'Send Code' again.")]
    [SerializeField] private float resendCooldownSeconds = 30f;
    [Tooltip("Automatically hide the login panel when authenticated.")]
    [SerializeField] private bool autoHideLoginPanelOnAuth = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogUserDetails = true;
    [SerializeField] private bool debugLogRawJson = false;

    [Header("Events")]
    public UnityEvent onAuthenticated; // Fires after successful verify OR silent refresh

    // Current tokens
    public string AccessToken { get; private set; }
    public string RefreshToken { get; private set; }

    // PlayerPrefs keys (change if you want)
    const string PP_AT = "sb_access_token";
    const string PP_RT = "sb_refresh_token";
    float _cooldownUntil;

    // --------- Auth DTOs ---------
    [Serializable] private class OtpSendRequest { public string email; public string type = "email"; public bool create_user; }
    [Serializable] private class VerifyRequest { public string email; public string token; public string type = "email"; }
    [Serializable] private class AuthUser { public string id; public string email; }
    [Serializable] private class AuthResponse { public string access_token; public string token_type; public int expires_in; public string refresh_token; public AuthUser user; }
    [Serializable] private class RefreshRequest { public string refresh_token; }

    // --------- User details (GET /auth/v1/user) ---------
    [Serializable] private class UserAppMetadata { public string provider; public string[] providers; }
    [Serializable] private class IdentityData { public string email; public string sub; } // minimal subset
    [Serializable]
    private class Identity
    {
        public string id;
        public string identity_id;
        public string provider;
        public string created_at;
        public string last_sign_in_at;
        public string updated_at;
        public IdentityData identity_data;
    }
    [Serializable]
    private class UserResponse
    {
        public string id;
        public string email;
        public string phone;
        public string aud;
        public string role;
        public string created_at;
        public string updated_at;
        public string last_sign_in_at;
        public string email_confirmed_at;
        public string phone_confirmed_at;
        public bool is_anonymous;
        public UserAppMetadata app_metadata;
        public Identity[] identities;
    }

    void Awake()
    {
        if (sendCodeButton) sendCodeButton.onClick.AddListener(() => StartCoroutine(SendEmailOtp()));
        if (verifyButton) verifyButton.onClick.AddListener(() => StartCoroutine(VerifyEmailOtp()));

        // Load existing tokens if any
        if (PlayerPrefs.HasKey(PP_AT)) AccessToken = PlayerPrefs.GetString(PP_AT);
        if (PlayerPrefs.HasKey(PP_RT)) RefreshToken = PlayerPrefs.GetString(PP_RT);

        // Show login UI by default (we may hide it in Start() if silent login works)
        ShowLoginUI(true);
    }

    IEnumerator Start()
    {
        if (trySilentLoginOnStart && !string.IsNullOrEmpty(RefreshToken))
        {
            Debug.Log(RefreshToken);
            SetStatus("Restoring session...");
            bool result = false;
            yield return StartCoroutine(RefreshSession(ok => result = ok));
            if (result)
            {
                if (autoHideLoginPanelOnAuth) ShowLoginUI(false);
                onAuthenticated?.Invoke();
                yield break;
            }
            SetStatus("Session expired. Please sign in.");
        }
        else
        {
            SetStatus("Welcome! Please sign in.");
        }

        // Ensure login UI is visible if silent login didn't happen
        ShowLoginUI(true);
    }

    void Update()
    {
        if (sendCodeButton)
            sendCodeButton.interactable = Time.unscaledTime >= _cooldownUntil && !string.IsNullOrEmpty(projectUrl) && !string.IsNullOrEmpty(anonKey);
    }

    public IEnumerator SendEmailOtp()
    {
        var email = emailInput ? emailInput.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(email)) { SetStatus("Enter your email."); yield break; }

        SetStatus("Sending code...");
        ToggleInteractable(false);

        var bodyObj = new OtpSendRequest { email = email, create_user = createUserIfMissing };
        var json = JsonUtility.ToJson(bodyObj);
        using var req = new UnityWebRequest($"{projectUrl}/auth/v1/otp", "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("apikey", anonKey);
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            SetStatus($"Send failed: {req.responseCode} {req.error}\n{req.downloadHandler.text}");
        }
        else
        {
            _cooldownUntil = Time.unscaledTime + resendCooldownSeconds;
            SetStatus("Code sent! Check your email (and spam).");
        }

        ToggleInteractable(true);
    }

    public IEnumerator VerifyEmailOtp()
    {
        var email = emailInput ? emailInput.text.Trim() : string.Empty;
        var code = codeInput ? codeInput.text.Trim() : string.Empty;

        if (string.IsNullOrEmpty(email)) { SetStatus("Enter your email."); yield break; }
        if (string.IsNullOrEmpty(code)) { SetStatus("Enter the 6-digit code."); yield break; }

        SetStatus("Verifying...");
        ToggleInteractable(false);

        var bodyObj = new VerifyRequest { email = email, token = code };
        var json = JsonUtility.ToJson(bodyObj);
        using var req = new UnityWebRequest($"{projectUrl}/auth/v1/verify", "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("apikey", anonKey);
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Accept", "application/json");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            SetStatus($"Verify failed: {req.responseCode} {req.error}\n{req.downloadHandler.text}");
            ToggleInteractable(true);
            yield break;
        }

        var auth = JsonUtility.FromJson<AuthResponse>(req.downloadHandler.text);
        if (debugLogRawJson) Debug.Log($"[Auth Verify Raw] {req.downloadHandler.text}");

        if (auth == null || string.IsNullOrEmpty(auth.access_token))
        {
            SetStatus("Verify succeeded but no token returned.");
            ToggleInteractable(true);
            yield break;
        }

        AccessToken = auth.access_token;
        RefreshToken = auth.refresh_token;

        PlayerPrefs.SetString(PP_AT, AccessToken);
        PlayerPrefs.SetString(PP_RT, RefreshToken);
        PlayerPrefs.Save();

        SetStatus($"Signed in as {auth.user?.email ?? "user"}");
        ToggleInteractable(true);

        if (autoHideLoginPanelOnAuth) ShowLoginUI(false);
        onAuthenticated?.Invoke();

        if (debugLogUserDetails)
            StartCoroutine(FetchAndLogCurrentUser("after-verify"));
    }

    public IEnumerator RefreshSession(Action<bool> onDone = null)
    {
        if (string.IsNullOrEmpty(RefreshToken))
        {
            onDone?.Invoke(false);
            yield break;
        }

        var bodyObj = new RefreshRequest { refresh_token = RefreshToken };
        var json = JsonUtility.ToJson(bodyObj);

        using var req = new UnityWebRequest($"{projectUrl}/auth/v1/token?grant_type=refresh_token", "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("apikey", anonKey);
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Accept", "application/json");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            SetStatus($"Refresh failed: {req.responseCode} {req.error}");
            onDone?.Invoke(false);
            yield break;
        }

        var auth = JsonUtility.FromJson<AuthResponse>(req.downloadHandler.text);
        if (debugLogRawJson) Debug.Log($"[Auth Refresh Raw] {req.downloadHandler.text}");

        if (auth == null || string.IsNullOrEmpty(auth.access_token))
        {
            SetStatus("Refresh returned no token.");
            onDone?.Invoke(false);
            yield break;
        }

        AccessToken = auth.access_token;
        RefreshToken = auth.refresh_token;
        PlayerPrefs.SetString(PP_AT, AccessToken);
        PlayerPrefs.SetString(PP_RT, RefreshToken);
        PlayerPrefs.Save();

        SetStatus("Session restored.");
        onDone?.Invoke(true);

        if (debugLogUserDetails)
            StartCoroutine(FetchAndLogCurrentUser("after-refresh"));
    }

    // Remote sign-out (revokes current session) + local cleanup
    public IEnumerator SignOutRemote()
    {
        // Try server logout if we have a valid access token
        if (!string.IsNullOrEmpty(AccessToken))
        {
            using var req = new UnityWebRequest($"{projectUrl}/auth/v1/logout", "POST");
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("apikey", anonKey);
            req.SetRequestHeader("Authorization", $"Bearer {AccessToken}");
            yield return req.SendWebRequest();
            // We won't block on errors here; we'll still clear locally.
        }

        SignOutLocal();
        ShowLoginUI(true);
        SetStatus("Signed out.");
    }

    public void SignOutLocal()
    {
        AccessToken = null;
        RefreshToken = null;
        PlayerPrefs.DeleteKey(PP_AT);
        PlayerPrefs.DeleteKey(PP_RT);
    }

    // Example: use AccessToken for a DB call
    public IEnumerator ExampleGetScores()
    {
        if (string.IsNullOrEmpty(AccessToken))
        {
            SetStatus("Not authenticated.");
            yield break;
        }

        using var req = UnityWebRequest.Get($"{projectUrl}/rest/v1/Scores?select=Name,Score&order=Score.desc&limit=20");
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("apikey", anonKey);
        req.SetRequestHeader("Authorization", $"Bearer {AccessToken}");
        req.SetRequestHeader("Accept", "application/json");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
            SetStatus($"Scores error: {req.responseCode} {req.error}");
        else
            SetStatus($"Scores: {req.downloadHandler.text}");
    }

    // ---- Fetch & debug current user ----
    IEnumerator FetchAndLogCurrentUser(string reasonTag)
    {
        if (string.IsNullOrEmpty(AccessToken))
        {
            Debug.LogWarning("[Supabase] Fetch user skipped: no access token.");
            yield break;
        }

        using var req = UnityWebRequest.Get($"{projectUrl}/auth/v1/user");
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("apikey", anonKey);
        req.SetRequestHeader("Authorization", $"Bearer {AccessToken}");
        req.SetRequestHeader("Accept", "application/json");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[Supabase] User fetch failed ({reasonTag}): {req.responseCode} {req.error}\n{req.downloadHandler.text}");
            yield break;
        }

        var raw = req.downloadHandler.text;
        if (debugLogRawJson) Debug.Log($"[Supabase] /user raw ({reasonTag}): {raw}");

        UserResponse user = null;
        try { user = JsonUtility.FromJson<UserResponse>(raw); }
        catch (Exception e) { Debug.LogWarning($"[Supabase] Could not parse user JSON: {e.Message}\nRaw: {raw}"); }

        if (user == null)
        {
            Debug.Log("[Supabase] User parsed as null; enable raw JSON to inspect.");
            yield break;
        }

        var sb = new StringBuilder();
        sb.AppendLine("========== Supabase User ==========");
        sb.AppendLine($"Reason:          {reasonTag}");
        sb.AppendLine($"ID:              {user.id}");
        sb.AppendLine($"Email:           {user.email}");
        sb.AppendLine($"Phone:           {user.phone}");
        sb.AppendLine($"Role/Aud:        {user.role} / {user.aud}");
        sb.AppendLine($"Created At:      {user.created_at}");
        sb.AppendLine($"Last Sign-In:    {user.last_sign_in_at}");
        sb.AppendLine($"Email Confirmed: {user.email_confirmed_at}");
        sb.AppendLine($"Phone Confirmed: {user.phone_confirmed_at}");
        if (user.app_metadata != null)
        {
            sb.AppendLine($"Provider:        {user.app_metadata.provider}");
            if (user.app_metadata.providers != null && user.app_metadata.providers.Length > 0)
                sb.AppendLine($"Providers:       {string.Join(", ", user.app_metadata.providers)}");
        }
        if (user.identities != null && user.identities.Length > 0)
        {
            sb.AppendLine($"Identities:      {user.identities.Length}");
            for (int i = 0; i < user.identities.Length; i++)
            {
                var id = user.identities[i];
                sb.AppendLine($"  - [{i}] provider={id?.provider}, email={id?.identity_data?.email}, last_sign_in_at={id?.last_sign_in_at}");
            }
        }
        sb.AppendLine("===================================");
        Debug.Log(sb.ToString());

        SetStatus($"Signed in: {user.email ?? user.id}");
    }

    // --- helpers ---
    void ShowLoginUI(bool show)
    {
        if (loginPanel) loginPanel.SetActive(show);
        // You can also toggle other panels here if needed.
    }

    void ToggleInteractable(bool v)
    {
        if (sendCodeButton) sendCodeButton.interactable = v && Time.unscaledTime >= _cooldownUntil;
        if (verifyButton) verifyButton.interactable = v;
        if (emailInput) emailInput.interactable = v;
        if (codeInput) codeInput.interactable = v;
    }

    void SetStatus(string msg)
    {
        if (statusText) statusText.text = msg;
        else Debug.Log(msg);
    }
}
