using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Jelly_anim : MonoBehaviour
{
    private Animator animator;
    private void Start()
    {
        animator = GetComponent<Animator>();
    }

    private void OnTriggerEnter(Collider other)
    {
        animator. Play("Jelly_Hit");
    }

    private void OnTriggerExit(Collider other)
    {
        animator.Play("Jelly_Hit");
    } 


}
