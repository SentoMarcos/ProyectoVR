using UnityEngine;

public class SoundController : MonoBehaviour
{
    public AudioSource audioSource;

    public void PlaySound()
    {
        audioSource.Play();
    }
}
