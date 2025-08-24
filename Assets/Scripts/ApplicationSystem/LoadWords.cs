using System;
using System.Collections.Generic;
using UnityEngine;

public class LoadWords : MonoBehaviour
{
    private const string CategoriesPath = "WordsDataBase";

    [Header("Load-on-Start (optional)")]
    [SerializeField] private string[] categoriesToLoadAtStart;     // e.g. ["504_Lesson_1"]
    [SerializeField] private bool buildUIAfterLoadAtStart = true;

    [Header("UI Prefabs & Parents")]
    [SerializeField] private Transform listParent;                  // a Vertical Layout Group container
    [SerializeField] private WordContainer wordItemPrefab;          // prefab with WordContainer component

    [Header("Build Options")]
    [SerializeField] private bool clearListBeforeBuild = true;

    public readonly Dictionary<string, WordsDatabase> Loaded =
        new Dictionary<string, WordsDatabase>(StringComparer.OrdinalIgnoreCase);

    private void Start()
    {
        if (categoriesToLoadAtStart == null || categoriesToLoadAtStart.Length == 0) return;

        foreach (var cat in categoriesToLoadAtStart)
        {
            if (string.IsNullOrWhiteSpace(cat)) continue;
            if (LoadCategory(cat) && buildUIAfterLoadAtStart)
                BuildUIForCategory(cat);
        }
    }

    [ContextMenu("Debug/Reload First Category and Rebuild UI")]
    private void DebugReloadFirst()
    {
        if (categoriesToLoadAtStart == null || categoriesToLoadAtStart.Length == 0) return;
        var cat = categoriesToLoadAtStart[0];
        if (string.IsNullOrWhiteSpace(cat)) return;
        LoadCategory(cat);
        BuildUIForCategory(cat);
    }

    [ContextMenu("Debug/List TextAssets in WordsDataBase")]
    private void DebugListTextAssets()
    {
        var all = Resources.LoadAll<TextAsset>("WordsDataBase");
        Debug.Log($"[LoadWords] Found {all.Length} TextAssets under Resources/WordsDataBase:");
        foreach (var ta in all) Debug.Log(" - " + ta.name);
    }

    public bool LoadCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            Debug.LogWarning("LoadCategory: categoryName is null/empty.");
            return false;
        }

        string resPath = $"{CategoriesPath}/{categoryName}";
        TextAsset jsonAsset = Resources.Load<TextAsset>(resPath);

        if (jsonAsset == null)
        {
            Debug.LogWarning($"LoadCategory: No JSON found at Resources/{resPath}.json " +
                             "(make sure the file has .json extension and sits inside a Resources folder).");
            return false;
        }

        try
        {
            // Try flat schema: { "words": [...] }
            var flat = JsonUtility.FromJson<WordsDatabase>(jsonAsset.text);
            if (flat != null && flat.words != null && flat.words.Count > 0)
            {
                Loaded[categoryName] = flat;
                Debug.Log($"Loaded '{categoryName}' with {flat.words.Count} words (flat schema).");
                return true;
            }

            // Try lesson schema: { "lesson": n, "words": [...] }
            var lesson = JsonUtility.FromJson<LessonDatabase>(jsonAsset.text);
            if (lesson != null && lesson.words != null && lesson.words.Count > 0)
            {
                Loaded[categoryName] = new WordsDatabase { words = lesson.words };
                Debug.Log($"Loaded '{categoryName}' with {lesson.words.Count} words (lesson schema, lesson {lesson.lesson}).");
                return true;
            }

            Debug.LogWarning($"LoadCategory: JSON parsed but empty/unknown schema for '{categoryName}'.");
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"LoadCategory: Failed to parse JSON for '{categoryName}': {ex.Message}");
            return false;
        }
    }

    public void BuildUIForCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            Debug.LogWarning("BuildUIForCategory: categoryName is null/empty.");
            return;
        }

        if (!Loaded.TryGetValue(categoryName, out var db))
        {
            if (!LoadCategory(categoryName))
            {
                Debug.LogWarning($"BuildUIForCategory: Could not load '{categoryName}'.");
                return;
            }
            db = Loaded[categoryName];
        }

        if (listParent == null || wordItemPrefab == null)
        {
            Debug.LogWarning("BuildUIForCategory: listParent or wordItemPrefab is not assigned.");
            return;
        }

        if (clearListBeforeBuild)
        {
            for (int i = listParent.childCount - 1; i >= 0; i--)
                Destroy(listParent.GetChild(i).gameObject);
        }

        if (db.words == null || db.words.Count == 0) return;

        for (int i = 0; i < db.words.Count; i++)
        {
            var view = Instantiate(wordItemPrefab, listParent);
            view.Apply(db.words[i], i + 1); // WordContainer will Fa.faConvert() when assigning Persian
        }
    }

    public void ClearUIList()
    {
        if (!listParent) return;
        for (int i = listParent.childCount - 1; i >= 0; i--)
            Destroy(listParent.GetChild(i).gameObject);
    }
}
