using TMPro;
using UnityEngine;

public class GameSystem : MonoBehaviour
{
    public static int money = 100;
    public static int damagePartCount = 6;

    [SerializeField] TMP_Text moneyDisplayText;
    [SerializeField] TMP_Text damagePartDisplayText;

    void Start()
    {
        UpdateUI();
    }

    void Update()
    {
        HandleTestInputs();
    }

    private void HandleTestInputs()
    {
        
        if (Input.GetKeyDown(KeyCode.Q))
        {
            Debug.Log("Add money");
            money += 10;
            UpdateUI();
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            Debug.Log("Add damage part");
            damagePartCount += 10;
            UpdateUI();
        }
    }

   
    public void UpdateUI()
    {
        if (moneyDisplayText != null)
            moneyDisplayText.text = "Money: " + money;

        if (damagePartDisplayText != null)
            damagePartDisplayText.text = "Damage Parts: " + damagePartCount;
    }
}