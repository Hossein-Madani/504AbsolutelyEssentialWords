using System.Collections.Generic;
using UnityEngine;

public class CategoryButtonsManager : MonoBehaviour
{
    public static CategoryButtonsManager Instance { get; private set; }

    [Header("Category Loader")]
    [SerializeField] private LoadWords loader;

    [Header("All Category Buttons (optional)")]
    [SerializeField] private List<CategoryButton> categoryButtons = new();

    private CategoryButton lastCategoryButton;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Duplicate CategoryButtonsManager destroyed.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void SelectCategory(CategoryButton btn)
    {
        if (lastCategoryButton == btn) return;

        if (lastCategoryButton != null)
            lastCategoryButton.SetSelectionState(false);

        lastCategoryButton = btn;
        btn.SetSelectionState(true);

        if (!loader)
        {
            Debug.LogWarning($"[{nameof(CategoryButtonsManager)}] No loader assigned.", this);
            return;
        }

        loader.BuildUIForCategory(btn.CategoryName);
    }

    public void UnselectAll()
    {
        foreach (var b in categoryButtons)
        {
            if (b) b.SetSelectionState(false);
        }
        lastCategoryButton = null;
    }
}
