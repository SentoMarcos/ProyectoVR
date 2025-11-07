using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ButtonImageSwap : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("Referencia al componente Image del boton")]
    public Image buttonImage;

    [Header("Sprites")]
    public Sprite normalSprite;   // Imagen por defecto
    public Sprite pressedSprite;  // Imagen cuando se presiona

    // Cambia la imagen cuando el boton se presiona
    public void OnPointerDown(PointerEventData eventData)
    {
        if (buttonImage != null && pressedSprite != null)
        {
            buttonImage.sprite = pressedSprite;
        }
    }

    // Vuelve a la imagen original cuando se suelta el boton
    public void OnPointerUp(PointerEventData eventData)
    {
        if (buttonImage != null && normalSprite != null)
        {
            buttonImage.sprite = normalSprite;
        }
    }
}
