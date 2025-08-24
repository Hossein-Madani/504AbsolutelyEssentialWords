using UnityEngine;
using UnityEngine.EventSystems;

public class TitleBarButton : MonoBehaviour , IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] Animator animator;
    private static readonly int EnterHash = Animator.StringToHash("Enter");
    private static readonly int ExitHash = Animator.StringToHash("Exit");

    public void OnPointerEnter(PointerEventData _)
    {
       animator.SetTrigger(EnterHash);
    }

    public void OnPointerExit(PointerEventData _)
    {
        animator.SetTrigger(ExitHash);
    }
}
