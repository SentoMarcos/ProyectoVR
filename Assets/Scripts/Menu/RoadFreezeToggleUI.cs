using UnityEngine;
using UnityEngine.UI;

public class RoadFreezeToggleUI : MonoBehaviour
{
    [Header("References")]
    public RoadFromTargetsSticky road;
    public Button button;
    public Text labelLegacy; // For Text (Legacy)
    public TMPro.TextMeshProUGUI labelTMP; // For TextMeshPro

    [Header("Labels")]
    public string labelFreeze = "Fijar";     // when update is ON, button action will Freeze
    public string labelUnfreeze = "Desfijar"; // when frozen, button action will Unfreeze

    [Header("Behavior")]
    [Tooltip("Además de activar 'manualFreeze', alterna el checkbox 'Update If Change' en el componente para que se vea en el inspector.")]
    public bool alsoToggleUpdateIfChange = true;
    [Tooltip("Recupera el valor previo de 'Update If Change' al desfijar. Si no hay valor previo, vuelve a true.")]
    public bool rememberPreviousUpdateIfChange = true;
    bool? cachedPrevUpdateIfChange;

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
    }

    void Awake()
    {
        if (!button) button = GetComponent<Button>();
        if (button) button.onClick.AddListener(OnClick);
        RefreshLabel();
    }

    void OnEnable()
    {
        RefreshLabel();
    }

    public void OnClick()
    {
        if (!road) { RefreshLabel(); return; }
        bool isFrozen = road.manualFreeze || (alsoToggleUpdateIfChange && !road.updateIfChange);
        if (!isFrozen)
        {
            // Fijar: activa manualFreeze y opcionalmente desactiva updateIfChange
            road.manualFreeze = true;
            if (alsoToggleUpdateIfChange)
            {
                if (rememberPreviousUpdateIfChange) cachedPrevUpdateIfChange = road.updateIfChange;
                road.updateIfChange = false;
            }
        }
        else
        {
            // Desfijar: desactiva manualFreeze y restaura updateIfChange
            road.manualFreeze = false;
            if (alsoToggleUpdateIfChange)
            {
                if (rememberPreviousUpdateIfChange && cachedPrevUpdateIfChange.HasValue)
                    road.updateIfChange = cachedPrevUpdateIfChange.Value;
                else
                    road.updateIfChange = true;
            }
        }
        RefreshLabel();
    }

    void RefreshLabel()
    {
        bool frozen = road && (road.manualFreeze || (alsoToggleUpdateIfChange && !road.updateIfChange));
        string text = frozen ? labelUnfreeze : labelFreeze;
        if (labelTMP) labelTMP.text = text;
        if (labelLegacy) labelLegacy.text = text;
    }
}
