using UnityEngine;

public class ClickEffectAnimator : MonoBehaviour
{
    [Tooltip("Arrossega aquí tots els fotogrames tallats de la imatge")]
    public Sprite[] frames;

    [Tooltip("Velocitat de l'animació (30 o 45 va perfecte per TFT)")]
    public float framesPerSecond = 35f;

    private SpriteRenderer spriteRenderer;
    private float timer;
    private int currentFrame;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (frames.Length > 0)
            spriteRenderer.sprite = frames[0];
    }

    void Update()
    {
        if (frames == null || frames.Length == 0) return;

        timer += Time.deltaTime;
        if (timer >= 1f / framesPerSecond)
        {
            timer -= 1f / framesPerSecond;
            currentFrame++;

            // Quan arriba a l'últim fotograma, es destrueix sol a l'instant!
            if (currentFrame >= frames.Length)
            {
                Destroy(gameObject);
            }
            else
            {
                spriteRenderer.sprite = frames[currentFrame];
            }
        }
    }
}