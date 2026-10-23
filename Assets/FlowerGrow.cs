using System.Collections;
using UnityEngine;

public class FlowerGrow : MonoBehaviour
{
    public float growDuration = 0.6f;
    public float lifetime = 10f;

    private Vector3 targetScale;

    void Awake()
    {
        targetScale = transform.localScale;
        if (targetScale.magnitude < 0.01f)
            targetScale = Vector3.one;
    }

    void Start()
    {
        transform.localScale = Vector3.zero;
        StartCoroutine(GrowCoroutine());
    }

    IEnumerator GrowCoroutine()
    {
        float elapsed = 0f;

        while (elapsed < growDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / growDuration);
            transform.localScale = targetScale * EaseOutBack(t);
            yield return null;
        }

        transform.localScale = targetScale;
        Destroy(gameObject, lifetime);
    }

    float EaseOutBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}