using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class CategoryButton : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Category")]
    [SerializeField] private string category = "Fruits";
    public string CategoryName => category;

    [Header("Animator (optional)")]
    [SerializeField] private Animator animator;

    // Pointer triggers (hashed)
    private static readonly int EnterHash = Animator.StringToHash("PointerEnter");
    private static readonly int ExitHash = Animator.StringToHash("PointerExit");
    private static readonly int ClickHash = Animator.StringToHash("PointerClick");

    private bool isSelected = false;

    private void Awake()
    {
        if (!animator) animator = GetComponent<Animator>();
    }

    public void OnPointerEnter(PointerEventData _)
    {
        if (!isSelected && animator) animator.SetTrigger(EnterHash);
    }

    public void OnPointerExit(PointerEventData _)
    {
        if (!isSelected && animator) animator.SetTrigger(ExitHash);
    }

    public void OnPointerClick(PointerEventData _)
    {
        if (isSelected) return;

        isSelected = true;
        animator?.SetTrigger(ClickHash);

        // 🔹 Call static manager directly
        if (CategoryButtonsManager.Instance != null)
        {
            CategoryButtonsManager.Instance.SelectCategory(this);
        }
        else
        {
            Debug.LogWarning("No CategoryButtonsManager in scene!");
        }
    }

    public void SetSelectionState(bool value)
    {
        isSelected = value;
        if (animator) animator.SetTrigger(value ? ClickHash : ExitHash);
    }
}
