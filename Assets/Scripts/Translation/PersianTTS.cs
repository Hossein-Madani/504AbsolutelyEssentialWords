using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class ButtonTTS : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField inputField;      // type text here
    public TMP_Text statusText;            // optional status label

    [Header("Language")]
    [Tooltip("Language code (e.g., 'fa' for Persian, 'en' for English, 'de' for German)")]
    public string languageCode = "fa";     // used for Google TTS voice
    [Tooltip("VoiceRSS locale (for Persian use 'fa-ir')")]
    public string voiceRssLocale = "fa-ir";

    [Header("Google (unofficial) - no key")]
    [Tooltip("Max characters per chunk (keep <200)")]
    public int googleChunkSize = 180;

    [Header("VoiceRSS (hosted, free key)")]
    [Tooltip("Get a free key at https://www.voicerss.org/")]
    public string voiceRssApiKey = "";     // put your free key here
    [Tooltip("Codec for VoiceRSS (MP3 recommended)")]
    public string voiceRssCodec = "MP3";   // MP3, WAV, OGG

    [Header("Networking")]
    public int timeoutSec = 15;

    private AudioSource audioSource;
    private Coroutine speakRoutine;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (!audioSource) audioSource = gameObject.AddComponent<AudioSource>();
    }

    // Hook this to your Button's OnClick
    public void OnSpeakClick()
    {
        if (speakRoutine != null) StopCoroutine(speakRoutine);

        string text = inputField ? inputField.text : "";
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Enter text first.");
            return;
        }

        SetStatus("Speaking...");
        speakRoutine = StartCoroutine(SpeakWithFallback(text.Trim()));
    }

    private IEnumerator SpeakWithFallback(string text)
    {
        // 1) Try Google (unofficial, keyless)
        bool ok = false;
        foreach (var chunk in ChunkForTTS(text, Mathf.Max(40, googleChunkSize)))
        {
            // extra params to reduce 400s
            string url = "https://translate.google.com/translate_tts"
                       + "?ie=UTF-8&client=tw-ob"
                       + "&ttsspeed=1"
                       + $"&q={UnityWebRequest.EscapeURL(chunk)}"
                       + $"&tl={UnityWebRequest.EscapeURL(languageCode)}";

            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
            {
                req.timeout = Mathf.Max(1, timeoutSec);
                req.SetRequestHeader("User-Agent", "Mozilla/5.0 (Unity)");

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    // Fail Google — break out to VoiceRSS fallback
                    Debug.LogWarning($"Google TTS failed ({req.responseCode}): {req.error}");
                    ok = false;
                    break;
                }

                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null) { Debug.LogWarning("Google TTS returned empty clip."); ok = false; break; }

                audioSource.clip = clip;
                audioSource.Play();
                while (audioSource.isPlaying) yield return null;
                ok = true;
            }
        }

        if (ok)
        {
            SetStatus("Done.");
            speakRoutine = null;
            yield break;
        }

        // 2) Fallback: VoiceRSS (hosted) — needs free API key
        if (string.IsNullOrEmpty(voiceRssApiKey))
        {
            SetStatus("Google TTS blocked. Add a free VoiceRSS API key to use fallback.");
            speakRoutine = null;
            yield break;
        }

        // Build VoiceRSS request
        string urlVR = "https://api.voicerss.org/"
                     + $"?key={UnityWebRequest.EscapeURL(voiceRssApiKey)}"
                     + $"&hl={UnityWebRequest.EscapeURL(string.IsNullOrEmpty(voiceRssLocale) ? languageCode : voiceRssLocale)}"
                     + $"&src={UnityWebRequest.EscapeURL(text)}"
                     + $"&c={UnityWebRequest.EscapeURL(voiceRssCodec.ToUpper())}"
                     + "&f=44khz_16bit_stereo";

        AudioType at = AudioType.MPEG;
        if (voiceRssCodec.ToUpper() == "WAV") at = AudioType.WAV;
        else if (voiceRssCodec.ToUpper() == "OGG") at = AudioType.OGGVORBIS;

        using (var req = UnityWebRequestMultimedia.GetAudioClip(urlVR, at))
        {
            req.timeout = Mathf.Max(1, timeoutSec);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                SetStatus($"VoiceRSS failed ({req.responseCode}): {req.error}");
                speakRoutine = null;
                yield break;
            }

            // VoiceRSS returns text body on error (e.g., invalid key); guard:
            string contentType = req.GetResponseHeader("Content-Type");
            if (contentType != null && contentType.Contains("text"))
            {
                string err = req.downloadHandler.text;
                SetStatus("VoiceRSS error: " + err);
                speakRoutine = null;
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(req);
            if (clip == null)
            {
                SetStatus("VoiceRSS returned empty clip.");
                speakRoutine = null;
                yield break;
            }

            audioSource.clip = clip;
            audioSource.Play();
            while (audioSource.isPlaying) yield return null;

            SetStatus("Done.");
            speakRoutine = null;
        }
    }

    private void SetStatus(string msg)
    {
        if (statusText) statusText.text = msg;
        Debug.Log(msg);
    }

    // Split long text into chunks (tries to break on spaces)
    private IEnumerable<string> ChunkForTTS(string text, int maxLen)
    {
        text = text.Trim();
        if (text.Length <= maxLen) { yield return text; yield break; }

        int start = 0;
        while (start < text.Length)
        {
            int len = Mathf.Min(maxLen, text.Length - start);
            int end = start + len;

            if (end < text.Length && text[end] != ' ')
            {
                int lastSpace = text.LastIndexOf(' ', end - 1, len);
                if (lastSpace > start + 20) end = lastSpace;
            }

            string piece = text.Substring(start, end - start).Trim();
            if (!string.IsNullOrEmpty(piece)) yield return piece;

            start = end;
            while (start < text.Length && text[start] == ' ') start++;
        }
    }
}
