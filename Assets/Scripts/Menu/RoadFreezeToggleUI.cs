using UnityEngine;
using UnityEngine.UI;

public class RoadFreezeToggleUI : MonoBehaviour
{
    [Header("Referencias")]
    public RoadFromTargetsSticky road;
    public Button button;
    public Image buttonImage;

    [Header("Sprites")]
    public Sprite normalSprite;   // Imagen por defecto
    public Sprite toggledSprite;  // Imagen cuando se activa

    [Header("Comportamiento de la carretera")]
    [Tooltip("Además de activar 'manualFreeze', alterna el checkbox 'Update If Change' en el componente para que se vea en el inspector.")]
    public bool alsoToggleUpdateIfChange = true;
    [Tooltip("Recupera el valor previo de 'Update If Change' al desfijar. Si no hay valor previo, vuelve a true.")]
    public bool rememberPreviousUpdateIfChange = true;

    private bool? cachedPrevUpdateIfChange;

    void Reset()
    {
        button = GetComponent<Button>();
        if (!button) button = gameObject.AddComponent<Button>();
        if (!road)
        {
#if UNITY_2023_1_OR_NEWER
            road = Object.FindFirstObjectByType<RoadFromTargetsSticky>();
#else
            road = Object.FindObjectOfType<RoadFromTargetsSticky>();
#endif
        }
        if (!buttonImage)
            buttonImage = GetComponent<Image>();
    }

    void Awake()
    {
        if (!button) button = GetComponent<Button>();
        if (button) button.onClick.AddListener(OnClick);
    }

    public void OnClick()
    {
        if (!road) return;

        bool isFrozen = road.manualFreeze || (alsoToggleUpdateIfChange && !road.updateIfChange);

        if (!isFrozen)
        {
            // Fijar carretera
            road.manualFreeze = true;
            if (alsoToggleUpdateIfChange)
            {
                if (rememberPreviousUpdateIfChange)
                    cachedPrevUpdateIfChange = road.updateIfChange;
                road.updateIfChange = false;
            }

            // Cambiar imagen a la activada
            if (buttonImage && toggledSprite)
                buttonImage.sprite = toggledSprite;
        }
        else
        {
            // Desfijar carretera
            road.manualFreeze = false;
            if (alsoToggleUpdateIfChange)
            {
                if (rememberPreviousUpdateIfChange && cachedPrevUpdateIfChange.HasValue)
                    road.updateIfChange = cachedPrevUpdateIfChange.Value;
                else
                    road.updateIfChange = true;
            }

            // Volver a la imagen normal
            if (buttonImage && normalSprite)
                buttonImage.sprite = normalSprite;
        }
    }
}
