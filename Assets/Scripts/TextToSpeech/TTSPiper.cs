// Assets/Scripts/TextToSpeech/TTSPiper.cs
// Unity Piper TTS: offline, runtime, robust. Windows/Linux friendly.
// Put piper + voice files in Assets/StreamingAssets/piper/
//
// Folder must contain at least:
//   piper.exe (or 'piper')
//   onnxruntime.dll
//   onnxruntime_providers_shared.dll
//   libespeak-ng.dll
//   espeak-ng-data/        (folder)
//   en_US-amy-medium.onnx
//   en_US-amy-medium.onnx.json   <-- MUST match the .onnx name exactly

using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine.UI;
using TMPro;

public class TTSPiper : MonoBehaviour
{
    public static TTSPiper Instance { get; private set; }
    [Header("Piper voice (files in Assets/StreamingAssets/piper/)")]
    // Good medium voices to try: "en_US-amy-medium.onnx", "en_US-lessac-medium.onnx", "en_GB-alan-medium.onnx"
    public string modelFileName = "en_US-ryan-high.onnx";

    [Tooltip("Leave at -1 to use model defaults. Typical clean values: Noise 0.30, Noise-W 0.60, Length 1.0")]
    [Range(-1f, 3f)] public float lengthScale = -1f; // <1 faster speech, >1 slower
    [Range(-1f, 2f)] public float noiseScale = -1f; // ~0.25–0.35 is clean
    [Range(-1f, 2f)] public float noiseW = -1f; // ~0.55–0.70 is natural

    [Header("Runtime behavior")]
    [Tooltip("Prefer temp WAV (auto-deleted). Some Piper builds produce noisy stdout; this avoids that.")]
    public bool preferTempFile = true;
    [Tooltip("Interrupt current speech when Speak is called again.")]
    public bool interruptOnNewSpeak = true;
    [Tooltip("Chunk long text into smaller sentences (keeps UI responsive).")]
    public bool chunkLongText = true;
    [Tooltip("Max characters per chunk when chunking.")]
    [Min(80)] public int maxChunkChars = 240;
    [Tooltip("Silence inserted between chunks (seconds).")]
    [Range(0f, 0.2f)] public float gapBetweenChunks = 0.03f;
    [Tooltip("Apply tiny fade in/out (seconds) to avoid edge clicks).")]
    [Range(0f, 0.05f)] public float edgeFadeSeconds = 0.01f;

    [Header("UI (optional)")]
    public TMP_InputField inputField;
    public Button speakButton;
    [TextArea] public string textToSpeak = "Hello from Piper (medium voice)!";

    // ---- private state ----
    private AudioSource _src;
    private CancellationTokenSource _cts;          // cancel current speak
    private readonly SemaphoreSlim _synthLock = new SemaphoreSlim(1, 1); // avoid overlapping Piper runs

    void Awake()
    {
        Instance = this;
        _src = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        _src.playOnAwake = false;
    }

    void Start()
    {
        if (speakButton) speakButton.onClick.AddListener(OnSpeakButtonPressed);
    }

    void OnDestroy() { CancelSpeaking(); }
    void OnDisable() { CancelSpeaking(); }


    public void OnSpeakButtonPressed()
    {
        var txt = (inputField && !string.IsNullOrWhiteSpace(inputField.text))
            ? inputField.text
            : textToSpeak;

        if (!string.IsNullOrWhiteSpace(txt))
            _ = Speak(txt);
    }


    public void SpeackThisText(string value) {
        var txt = (value!=null)
                ? value
                : textToSpeak;

        if (!string.IsNullOrWhiteSpace(txt))
            _ = Speak(txt);
    }

    /// <summary>Public entry—speaks the given text.</summary>
    public async Task Speak(string text)
    {
        if (interruptOnNewSpeak) CancelSpeaking();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            // Preprocess & chunk if requested
            var chunks = chunkLongText ? ChunkText(CleanInput(text), maxChunkChars) : new List<string> { CleanInput(text) };

            // Synthesize each chunk, collect samples, stitch into one clip (with optional silence gaps)
            var allSamples = new List<float>();
            int channels = 1;
            int sampleRate = 22050;
            int gapSamples = 0;

            foreach (var chunk in chunks)
            {
                token.ThrowIfCancellationRequested();
                var result = await SynthesizeOne(chunk);
                if (result == null) continue;

                var (samples, ch, sr) = result.Value;
                if (samples == null || samples.Length == 0) continue;

                // Ensure consistent format
                if (allSamples.Count == 0)
                {
                    channels = ch;
                    sampleRate = sr;
                    gapSamples = Mathf.RoundToInt(gapBetweenChunks * sampleRate) * channels;
                }

                // Edge fade
                ApplyEdgeFade(samples, channels, sampleRate, edgeFadeSeconds);

                // Add gap if not first chunk
                if (allSamples.Count > 0 && gapSamples > 0)
                    allSamples.AddRange(new float[gapSamples]);

                allSamples.AddRange(samples);
            }

            token.ThrowIfCancellationRequested();

            if (allSamples.Count == 0) return;

            // Create and play the clip
            var clip = AudioClip.Create("PiperClip", allSamples.Count / channels, channels, sampleRate, false);
            clip.SetData(allSamples.ToArray(), 0);
            _src.clip = clip;
            _src.Play();
        }
        catch (OperationCanceledException) { /* interrupted, ignore */ }
        catch (Exception ex) { UnityEngine.Debug.LogError("Piper Speak error: " + ex.Message); }
    }

    public void CancelSpeaking()
    {
        try { _cts?.Cancel(); } catch { }
        if (_src && _src.isPlaying) _src.Stop();
    }

    // ---------- Synthesis helpers ----------

    private async Task<(float[] samples, int channels, int sampleRate)?> SynthesizeOne(string text)
    {
        await _synthLock.WaitAsync();
        try
        {
            var (piperExe, modelPath) = ResolvePaths();
            if (piperExe == null || modelPath == null) return null;

            // Prefer clean temp-file path if toggled, else try stdout then fallback
            byte[] wavBytes = null;

            if (!preferTempFile)
                wavBytes = await SynthesizeStdout(piperExe, modelPath, text);

            if (wavBytes == null)
                wavBytes = await SynthesizeTempFile(piperExe, modelPath, text);

            if (wavBytes == null || wavBytes.Length < 44)
            {
                UnityEngine.Debug.LogWarning("Piper returned invalid audio.");
                return null;
            }

            if (!TryDecodeWav(wavBytes, out var samples, out int channels, out int sampleRate))
                return null;

            return (samples, channels, sampleRate);
        }
        finally { _synthLock.Release(); }
    }

    private (string piperExe, string modelPath) ResolvePaths()
    {
        string piperDir = Path.Combine(Application.streamingAssetsPath, "piper");
        string piperExe = Path.Combine(piperDir, "piper.exe");
        if (!File.Exists(piperExe))
            piperExe = Path.Combine(piperDir, "piper"); // Linux/mac fallback

        string modelPath = Path.Combine(piperDir, modelFileName);
        string modelJson = modelPath + ".json";

        if (!File.Exists(piperExe)) { UnityEngine.Debug.LogError("piper executable not found at: " + piperExe); return (null, null); }
        if (!File.Exists(modelPath)) { UnityEngine.Debug.LogError("Piper model not found: " + modelPath); return (null, null); }
        if (!File.Exists(modelJson)) { UnityEngine.Debug.LogError("Piper model JSON not found: " + modelJson); return (null, null); }

        return (piperExe, modelPath);
    }

    private async Task<byte[]> SynthesizeStdout(string exe, string modelPath, string text)
    {
        try
        {
            string workDir = Path.GetDirectoryName(exe);
            string Arg(string s) => $"\"{s.Replace("\"", "\\\"")}\"";

            string args = $"--model {Arg(modelPath)} --output_file -";
            // Include overrides only if >= 0
            if (lengthScale >= 0.05f) args += $" --length_scale {lengthScale}";
            if (noiseScale >= 0f) args += $" --noise_scale {noiseScale}";
            if (noiseW >= 0f) args += $" --noise_w {noiseW}";

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workDir,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // Use all cores (helps speed on CPU)
            string cores = Environment.ProcessorCount.ToString();
            psi.EnvironmentVariables["OMP_NUM_THREADS"] = cores;
            psi.EnvironmentVariables["MKL_NUM_THREADS"] = cores;
            psi.EnvironmentVariables["ORT_NUM_THREADS"] = cores;

            using var proc = Process.Start(psi);
            await proc.StandardInput.WriteAsync(text);
            proc.StandardInput.Close();

            using var ms = new MemoryStream(1 << 20);
            var copyTask = proc.StandardOutput.BaseStream.CopyToAsync(ms);
            var errTask = proc.StandardError.ReadToEndAsync();

            await Task.Run(() => proc.WaitForExit());
            await Task.WhenAll(copyTask, errTask);

            if (proc.ExitCode != 0)
            {
                UnityEngine.Debug.LogWarning($"Piper stdout failed (exit {proc.ExitCode}). STDERR:\n{errTask.Result}");
                return null;
            }
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("Stdout synth exception: " + ex.Message);
            return null;
        }
    }

    private async Task<byte[]> SynthesizeTempFile(string exe, string modelPath, string text)
    {
        string workDir = Path.GetDirectoryName(exe);
        string tmp = Path.Combine(Application.temporaryCachePath, $"piper_{DateTime.UtcNow.Ticks}.wav");

        try
        {
            string Arg(string s) => $"\"{s.Replace("\"", "\\\"")}\"";
            string args = $"--model {Arg(modelPath)} --output_file {Arg(tmp)}";
            if (lengthScale >= 0.05f) args += $" --length_scale {lengthScale}";
            if (noiseScale >= 0f) args += $" --noise_scale {noiseScale}";
            if (noiseW >= 0f) args += $" --noise_w {noiseW}";

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workDir,
                RedirectStandardInput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            await proc.StandardInput.WriteAsync(text);
            proc.StandardInput.Close();

            var errTask = proc.StandardError.ReadToEndAsync();
            await Task.Run(() => proc.WaitForExit());

            if (proc.ExitCode != 0)
            {
                UnityEngine.Debug.LogError($"Piper temp-file failed (exit {proc.ExitCode}). STDERR:\n{errTask.Result}");
                return null;
            }
            if (!File.Exists(tmp))
            {
                UnityEngine.Debug.LogError("Piper did not create the temp WAV: " + tmp);
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(tmp);
            try { File.Delete(tmp); } catch { /* ignore */ }
            return bytes;
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError("Temp-file synth exception: " + ex.Message);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return null;
        }
    }

    // ---------- Audio utils ----------

    // Minimal PCM16 WAV decoder (mono/stereo)
    private bool TryDecodeWav(byte[] data, out float[] samples, out int channels, out int sampleRate)
    {
        samples = null; channels = 1; sampleRate = 22050;
        try
        {
            using var br = new BinaryReader(new MemoryStream(data, false), Encoding.UTF8, false);
            if (new string(br.ReadChars(4)) != "RIFF") return false;
            br.ReadUInt32(); // file size
            if (new string(br.ReadChars(4)) != "WAVE") return false;

            ushort fmt = 1, bps = 16; uint dataSize = 0; long dataPos = 0;

            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                string id = new string(br.ReadChars(4));
                uint size = br.ReadUInt32();
                long next = br.BaseStream.Position + size;

                if (id == "fmt ")
                {
                    fmt = br.ReadUInt16();
                    channels = br.ReadUInt16();
                    sampleRate = (int)br.ReadUInt32();
                    br.ReadUInt32(); // byteRate
                    br.ReadUInt16(); // blockAlign
                    bps = br.ReadUInt16();
                }
                else if (id == "data")
                {
                    dataSize = size;
                    dataPos = br.BaseStream.Position;
                }
                br.BaseStream.Position = next;
                if (dataPos != 0) break;
            }

            if (fmt != 1 || bps != 16 || dataPos == 0 || dataSize == 0) return false;

            int count = (int)(dataSize / 2);
            var outF = new float[count];
            int pos = (int)dataPos;
            for (int i = 0; i < count; i++)
            {
                short s = (short)(data[pos] | (data[pos + 1] << 8));
                outF[i] = s / 32768f;
                pos += 2;
            }
            samples = outF;
            return true;
        }
        catch { return false; }
    }

    private void ApplyEdgeFade(float[] samples, int channels, int sampleRate, float fadeSeconds)
    {
        if (samples == null || samples.Length == 0 || fadeSeconds <= 0f) return;
        int fade = Mathf.Max(1, Mathf.RoundToInt(sampleRate * fadeSeconds)) * channels;
        int N = samples.Length;

        // Fade in
        for (int i = 0; i < fade && i < N; i++)
            samples[i] *= i / (float)fade;

        // Fade out
        for (int i = 0; i < fade && i < N; i++)
        {
            int idx = N - 1 - i;
            if (idx < 0) break;
            samples[idx] *= i / (float)fade;
        }
    }

    private static readonly Regex SentenceSplit =
        new Regex(@"(?<=[\.\!\?\n])\s+|(?<=,)\s+", RegexOptions.Compiled);

    private List<string> ChunkText(string text, int maxLen)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return parts;

        // Split on sentence-ish boundaries first
        var raw = SentenceSplit.Split(text);
        var sb = new StringBuilder();

        foreach (var piece in raw)
        {
            var add = piece.Trim();
            if (add.Length == 0) continue;

            if (sb.Length + add.Length + 1 <= maxLen)
            {
                if (sb.Length > 0) sb.Append(" ");
                sb.Append(add);
            }
            else
            {
                if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); }
                // If single piece is huge, hard-wrap:
                for (int i = 0; i < add.Length; i += maxLen)
                    parts.Add(add.Substring(i, Math.Min(maxLen, add.Length - i)));
            }
        }
        if (sb.Length > 0) parts.Add(sb.ToString());
        return parts;
    }

    private string CleanInput(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        // Add a period if absolutely no end punctuation—helps prosody slightly.
        if (!Regex.IsMatch(s, @"[\.!\?]\s*$")) s += ".";
        return s;
    }
}
