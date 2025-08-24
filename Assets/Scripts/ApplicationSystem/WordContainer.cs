using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WordContainer : MonoBehaviour
{
    [Header("Expand/Collapse (Word Card Only)")]
    [SerializeField] private Button expandAndCollapseButton;
    [SerializeField] private Animator animator;
    [SerializeField] private string expandTrigger = "Expand";
    [SerializeField] private string collapseTrigger = "Collapse";

    [Header("Word Texts (Word Card Only)")]
    [SerializeField] private TMP_Text numberText;   // "1", "2", "3", ...
    [SerializeField] private TMP_Text englishText;
    [SerializeField] private TMP_Text persianText;

    [Header("Fixed 3 Example Rows (Word Card Only)")]
    [SerializeField] private bool hideUnusedExampleRows = true;

    [SerializeField] Button speackButton1;
    [SerializeField] Button speackButton2;
    [SerializeField] Button speackButton3;
    [SerializeField] Button speackButton4;
    [SerializeField] Button speackButton5;



    [Serializable]
    public class ExampleSentences
    {
        public GameObject row;
        public TMP_Text englishText;
        public TMP_Text persianText;

        public void Set(ExampleEntry ex, bool hideWhenNull)
        {
            if (ex != null)
            {
                if (englishText) englishText.text = ex.english ?? "";
                if (persianText) persianText.text = Fa.faConvert(ex.persian);
                if (row) row.SetActive(true);
            }
            else
            {
                if (englishText) englishText.text = "";
                if (persianText) persianText.text = "";
                if (row && hideWhenNull) row.SetActive(false);
            }
        }
    }

   
    [SerializeField] private ExampleSentences example1;
    [SerializeField] private ExampleSentences example2;
    [SerializeField] private ExampleSentences example3;
    [SerializeField] private ExampleSentences example4;
    [SerializeField] private ExampleSentences example5;

    private bool isExpanded = false;

    private void Start()
    {
        speackButton1.onClick.AddListener(() =>
        {
            if (example1.englishText.text != null && !string.IsNullOrEmpty(example1.englishText.text))
                TTSPiper.Instance.SpeackThisText(example1.englishText.text);
        });

        speackButton2.onClick.AddListener(() =>
        {
            if (example2.englishText.text != null && !string.IsNullOrEmpty(example2.englishText.text))
                TTSPiper.Instance.SpeackThisText(example2.englishText.text);
        });

        speackButton3.onClick.AddListener(() =>
        {
            if (example3.englishText.text != null && !string.IsNullOrEmpty(example3.englishText.text))
                TTSPiper.Instance.SpeackThisText(example3.englishText.text);
        });

        speackButton4.onClick.AddListener(() =>
        {
            if (example4.englishText.text != null && !string.IsNullOrEmpty(example4.englishText.text))
                TTSPiper.Instance.SpeackThisText(example4.englishText.text);
        });

        speackButton5.onClick.AddListener(() =>
        {
            if (example5.englishText.text != null && !string.IsNullOrEmpty(example5.englishText.text))
                TTSPiper.Instance.SpeackThisText(example5.englishText.text);
        });



        if (expandAndCollapseButton != null)
            expandAndCollapseButton.onClick.AddListener(ToggleExpandCollapse);
    }

    // Back-compat (no number passed)
    public void Apply(WordEntry entry) => Apply(entry, 0);

    // Apply with index (1,2,3,...)
    public void Apply(WordEntry entry, int number)
    {
        if (entry == null) return;

        if (numberText) numberText.text = number > 0 ? number.ToString() : "";
        if (englishText) englishText.text = entry.english ?? "";
        if (persianText) persianText.text = Fa.faConvert(entry.persian);

        var list = entry.examples ?? new List<ExampleEntry>();
        example1?.Set(list.Count > 0 ? list[0] : null, hideUnusedExampleRows);
        example2?.Set(list.Count > 1 ? list[1] : null, hideUnusedExampleRows);
        example3?.Set(list.Count > 2 ? list[2] : null, hideUnusedExampleRows);
        example4?.Set(list.Count > 3 ? list[3] : null, hideUnusedExampleRows);
        example5?.Set(list.Count > 4 ? list[4] : null, hideUnusedExampleRows);


    }

    private void ToggleExpandCollapse()
    {
        isExpanded = !isExpanded;
        if (!animator) return;

        if (isExpanded)
        {
            if (!string.IsNullOrEmpty(collapseTrigger)) animator.ResetTrigger(collapseTrigger);
            if (!string.IsNullOrEmpty(expandTrigger)) animator.SetTrigger(expandTrigger);
        }
        else
        {
            if (!string.IsNullOrEmpty(expandTrigger)) animator.ResetTrigger(expandTrigger);
            if (!string.IsNullOrEmpty(collapseTrigger)) animator.SetTrigger(collapseTrigger);
        }
    }
}
