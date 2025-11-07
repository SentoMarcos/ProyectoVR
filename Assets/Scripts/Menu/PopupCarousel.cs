using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PopupCarousel : MonoBehaviour
{
    [Header("UI References")]
    public GameObject popupPanel;
    public Image slideImage;

    [Header("Slides")]
    public List<Sprite> slides;

    [Header("Buttons")]
    public Button nextButton;
    public Button closeButton;

    public bool loop = false;

    private int index = 0;

    void Awake()
    {
        popupPanel.SetActive(false);
        closeButton.gameObject.SetActive(false); // hide close at start
    }

    public void OpenPopup()
    {
        index = 0;
        UpdateSlide();
        popupPanel.SetActive(true);
    }

    public void NextSlide()
    {
        if (index < slides.Count - 1)
        {
            index++;
        }
        else
        {
            // If loop mode = true, cycle back; else do nothing here
            if (loop) index = 0;
        }

        UpdateSlide();
    }

    public void ClosePopup()
    {
        popupPanel.SetActive(false);
    }

    private void UpdateSlide()
    {
        slideImage.sprite = slides[index];
        slideImage.SetNativeSize();

        // ✅ If on last slide → show Close button and hide Next
        if (index == slides.Count - 1)
        {
            nextButton.gameObject.SetActive(false);
            closeButton.gameObject.SetActive(true);
        }
        else
        {
            nextButton.gameObject.SetActive(true);
            closeButton.gameObject.SetActive(false);
        }
    }
}
