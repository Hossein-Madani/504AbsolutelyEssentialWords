using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ExampleEntry
{
    public string english;
    public string persian;
}

[Serializable]
public class WordEntry
{
    public string english;
    public string persian;
    public List<ExampleEntry> examples;
}

[Serializable]
public class WordsDatabase
{
    public List<WordEntry> words;
}

// Supports JSON like: { "lesson": 1, "words": [...] }
[Serializable]
public class LessonDatabase
{
    public int lesson;
    public List<WordEntry> words;
}
