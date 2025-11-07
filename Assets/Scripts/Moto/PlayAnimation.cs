using UnityEngine;

public class PlayAnimation : MonoBehaviour
{
    public Animator animator;

    public void Play(string animationName)
    {
        animator.Rebind();
        animator.Update(0f);
        animator.Play(animationName);
    }
}
