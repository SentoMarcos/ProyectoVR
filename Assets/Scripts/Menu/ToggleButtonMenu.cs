using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ToggleButtonMenu : MonoBehaviour, IPointerClickHandler
{
    [Header("Referencia al componente Image del bot�n")]
    public Image buttonImage;

    [Header("Sprites")]
    public Sprite normalSprite;   // Imagen por defecto
    public Sprite toggledSprite;  // Imagen cuando se activa

    [Header("GameObject de los botones secundarios")]
    public GameObject menuPanel;  // Panel que contiene los 4 botones
    public GameObject imagePanel;

    private bool isToggled = false;

    // Detecta el click del bot�n
    public void OnPointerClick(PointerEventData eventData)
    {
        if (buttonImage == null || menuPanel == null) return;

        if (!isToggled)
        {
            // Cambia a la imagen activada y muestra los botones
            buttonImage.sprite = toggledSprite;
            menuPanel.SetActive(true);
            imagePanel.SetActive(true);
            isToggled = true;
        }
        else
        {
            // Vuelve a la imagen normal y oculta los botones
            buttonImage.sprite = normalSprite;
            menuPanel.SetActive(false);
            imagePanel.SetActive(false);
            isToggled = false;
        }
    }
}
