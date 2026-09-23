using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public CharacterController controller;
    public float hiz = 6f;
    public float yercekimi = -25f; // Daha tok bir düþüþ için idealdir
    public float ziplamaYuksekligi = 2.5f;

    private Vector3 dikeyHiz;

    // Gecikmeyi çözen yeni deðiþkenler
    private float zeminZamanlayicisi;
    public float zeminToleransi = 0.15f; // Yere indikten sonraki 0.15 saniyelik algýlama payý

    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        // 1. ADIM: Zemin Kontrolü ve Zamanlayýcý
        if (controller.isGrounded)
        {
            zeminZamanlayicisi = zeminToleransi; // Yerdeyken zamanlayýcýyý sürekli doldurur

            if (dikeyHiz.y < 0)
            {
                dikeyHiz.y = -2f;
            }
        }
        else
        {
            // Havadaysa zamanlayýcý geriye doðru saymaya baþlar
            zeminZamanlayicisi -= Time.deltaTime;
        }

        // 2. ADIM: Yatay Hareket (W,A,S,D)
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        Vector3 hareketYonu = transform.right * x + transform.forward * z;
        controller.Move(hareketYonu * hiz * Time.deltaTime);

        // 3. ADIM: Zýplama Kontrolü (Gecikmesiz Yeni Mantýk)
        // Artýk sadece "isGrounded" deðil, tolerans süresine de bakýyoruz
        if (Input.GetButtonDown("Jump") && zeminZamanlayicisi > 0)
        {
            dikeyHiz.y = Mathf.Sqrt(ziplamaYuksekligi * -2f * yercekimi);
            zeminZamanlayicisi = 0; // Zýpladýðý an zamanlayýcýyý sýfýrlýyoruz ki havada tekrar zýplamasýn
        }

        // 4. ADIM: Yerçekimi Uygulama
        dikeyHiz.y += yercekimi * Time.deltaTime;
        controller.Move(dikeyHiz * Time.deltaTime);
    }
}
