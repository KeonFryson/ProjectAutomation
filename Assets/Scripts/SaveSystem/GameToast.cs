using UnityEngine;
using UnityEngine.UI;

/// <summary>Small "Autosaved" / "Saved to Slot 1" message that fades out at the top of the screen.</summary>
public class GameToast : MonoBehaviour
{
    private static GameToast instance;

    private Text label;
    private CanvasGroup group;
    private float timer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { instance = null; }

    public static void Show(string message, float seconds = 2f)
    {
        if (instance == null) instance = Create();
        instance.label.text = message;
        instance.timer = seconds;
        instance.group.alpha = 1f;
    }

    private static GameToast Create()
    {
        Canvas canvas = MenuUI.CreateCanvas("ToastCanvas", 200);
        var toast = canvas.gameObject.AddComponent<GameToast>();
        toast.group = canvas.gameObject.AddComponent<CanvasGroup>();
        toast.group.blocksRaycasts = false;
        toast.group.interactable = false;
        toast.group.alpha = 0f;

        RectTransform rt = MenuUI.MakePanel(canvas.transform, "Toast", MenuUI.PanelBg);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -20f);
        rt.sizeDelta = new Vector2(340f, 40f);
        rt.GetComponent<Image>().raycastTarget = false;

        Text text = MenuUI.MakeLabel(rt, "", 16, FontStyle.Bold, TextAnchor.MiddleCenter, 40f);
        MenuUI.Stretch(text.rectTransform);
        toast.label = text;
        return toast;
    }

    void Update()
    {
        if (timer <= 0f) return;
        timer -= Time.unscaledDeltaTime;
        group.alpha = Mathf.Clamp01(timer / 0.5f); // fade over the last half second
    }
}
