using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MoneySystem : MonoBehaviour
{
    public static int money = 100;
    [SerializeField] GameObject moneyDisplayObject;
    // Update is called once per frame
    public void Money()
    {
        moneyDisplayObject.GetComponent<TMPro.TMP_Text>().text = "Money: " + money;
        //Test add money
        if (Input.GetButtonDown("Fire1"))
        {
            Debug.Log("add money");
            money += 10;
        }

    }


    void Update()
    {
        Money();
        
    }
}
