using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro; // <- TextMeshPro

public class MyMemoryTranslateUI : MonoBehaviour
{
    private const string Api = "https://api.mymemory.translated.net/get";

    [Header("TextMeshPro UI")]
    public TMP_InputField inputField;   // User enters source text here
    public TMP_Text outputText;         // Translation appears here
    public TMP_Text statusText;         // Optional: status/errors

    [Header("Translation Settings")]
    [Tooltip("Source language code (e.g., 'de' for German, 'fa' for Persian)")]
    public string sourceLang = "de";
    [Tooltip("Target language code (e.g., 'en' for English)")]
    public string targetLang = "en";

    [Header("Optional: improves reliability with MyMemory")]
    [Tooltip("Optional email sent as 'de=' param; can help with rate limits. Leave empty to skip.")]
    public string contactEmail = ""; // e.g., "you@example.com"

    [Header("Networking")]
    [Tooltip("Per-attempt timeout (seconds)")]
    public int requestTimeoutSec = 10;
    [Tooltip("Max retry attempts on transient failures")]
    public int maxRetries = 3;

    [Header("Text-to-Speech (TTS)")]
    [Tooltip("Speak the translated result automatically")]
    public bool autoSpeakOnSuccess = true;
    [Tooltip("AudioSource to play speech. If empty, one will be added automatically.")]
    public AudioSource audioSource;
    [Tooltip("Override language for TTS (leave empty to use targetLang)")]
    public string ttsLangOverride = ""; // e.g., "en", "de", "fa"
    [Tooltip("Max chars per chunk for TTS (Google TTS ~200 char limit)")]
    public int ttsChunkSize = 180;

    private Coroutine running;

    // Hook this to a UI Button's OnClick in the Inspector
    public void OnTranslateClick()
    {
        if (running != null) StopCoroutine(running);
        var text = inputField != null ? inputField.text : null;

        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Please enter text to translate.");
            return;
        }

        EnsureAudioSource();

        SetStatus("Translating...");
        if (outputText) outputText.text = ""; // clear previous
        running = StartCoroutine(Translate(text.Trim(), sourceLang, targetLang, result =>
        {
            if (!string.IsNullOrEmpty(result))
            {
                if (outputText) outputText.text = result;
                SetStatus("Done");

                if (autoSpeakOnSuccess)
                {
                    string langForVoice = string.IsNullOrEmpty(ttsLangOverride) ? targetLang : ttsLangOverride;
                    StartCoroutine(SpeakText(result, langForVoice));
                }
            }
            else
            {
                SetStatus("Translation failed (see console).");
            }
            running = null;
        }));
    }

    private void OnDisable()
    {
        if (running != null) StopCoroutine(running);
    }

    private void SetStatus(string msg)
    {
        if (statusText) statusText.text = msg;
        Debug.Log(msg);
    }

    private void EnsureAudioSource()
    {
        if (audioSource == null)
        {
            audioSource = gameObject.GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    // ---------- Translation ----------
    public IEnumerator Translate(string text, string src, string tgt, System.Action<string> onDone)
    {
        float backoff = 0.75f;

        for (int attempt = 1; attempt <= Mathf.Max(1, maxRetries); attempt++)
        {
            string url = $"{Api}?q={UnityWebRequest.EscapeURL(text)}&langpair={src}|{tgt}";
            if (!string.IsNullOrEmpty(contactEmail))
                url += $"&de={UnityWebRequest.EscapeURL(contactEmail)}";

            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("Accept", "application/json");
                req.timeout = Mathf.Max(1, requestTimeoutSec);

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    var json = req.downloadHandler.text;
                    var resp = JsonUtility.FromJson<MyMemoryResponse>(json);

                    if (resp != null && resp.responseData != null && !string.IsNullOrEmpty(resp.responseData.translatedText))
                    {
                        if (resp.responseStatus == 200)
                        {
                            onDone?.Invoke(resp.responseData.translatedText);
                            yield break;
                        }
                        else
                        {
                            Debug.LogWarning($"MyMemory returned status {resp.responseStatus}: {resp.responseDetails}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("Unexpected response: " + json);
                    }
                }
                else
                {
                    Debug.LogWarning($"Request failed ({req.responseCode}): {req.error}\nBody: {req.downloadHandler.text}");
                }
            }

            if (attempt < maxRetries)
            {
                yield return new WaitForSeconds(backoff);
                backoff *= 2f; // exponential backoff
            }
        }

        Debug.LogError("MyMemory: all retries failed.");
        onDone?.Invoke(null);
    }

    // ---------- TTS (Google Translate TTS - unofficial) ----------
    // Splits long text into chunks and plays sequentially.
    private IEnumerator SpeakText(string fullText, string langCode)
    {
        if (string.IsNullOrWhiteSpace(fullText)) yield break;

        foreach (var chunk in ChunkForTTS(fullText, Mathf.Max(40, ttsChunkSize)))
        {
            string url = $"https://translate.google.com/translate_tts?ie=UTF-8&client=tw-ob&q={UnityWebRequest.EscapeURL(chunk)}&tl={UnityWebRequest.EscapeURL(langCode)}";

            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
            {
                // Some CDNs/proxies like a User-Agent header
                www.SetRequestHeader("User-Agent", "Mozilla/5.0 (Unity)");
                www.timeout = 10;

                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("TTS download failed: " + www.error);
                    continue; // try next chunk anyway
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (clip != null)
                {
                    audioSource.clip = clip;
                    audioSource.Play();

                    // wait until finished
                    while (audioSource.isPlaying) yield return null;
                }
            }
        }
    }

    // Simple chunker that tries to split on spaces without exceeding maxLen
    private IEnumerable<string> ChunkForTTS(string text, int maxLen)
    {
        text = text.Trim();
        if (text.Length <= maxLen) { yield return text; yield break; }

        int start = 0;
        while (start < text.Length)
        {
            int len = Mathf.Min(maxLen, text.Length - start);
            int end = start + len;

            // try to break at last space within the chunk
            if (end < text.Length && text[end] != ' ')
            {
                int lastSpace = text.LastIndexOf(' ', end - 1, len);
                if (lastSpace > start + 20) // avoid tiny fragments
                    end = lastSpace;
            }

            string piece = text.Substring(start, end - start).Trim();
            if (!string.IsNullOrEmpty(piece)) yield return piece;

            start = end;
            // skip any spaces
            while (start < text.Length && text[start] == ' ') start++;
        }
    }

    // ---------- DTOs ----------
    [System.Serializable]
    private class MyMemoryResponse
    {
        public ResponseData responseData;
        public int responseStatus;
        public string responseDetails;
    }

    [System.Serializable]
    private class ResponseData
    {
        public string translatedText;
        public double match;
    }
}
