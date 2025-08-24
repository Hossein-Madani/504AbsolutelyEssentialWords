using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class ApplicationSetup : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI fpsshower;
    
    private float deltaTime = 0.0f;
    void Start()
    {
   //  Application.targetFrameRate = 90;   
    }
    void Update()
    {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        float fps = 1.0f / deltaTime;
        fpsshower.text = $"FPS: {fps:0.}";
    }

}
