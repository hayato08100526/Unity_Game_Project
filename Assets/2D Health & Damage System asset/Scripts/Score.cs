using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public class ScoreGauge : MonoBehaviour
{
    int countMin;
    int countMax;
    int count;

    public InputField inputTextField;
    public Slider gauge;


    private void Start()
    {
        countMin = 0;
        countMax = 10;
        count = 10;

        gauge.minValue = countMin;
        gauge.maxValue = countMax;
        gauge.value = count;

        inputTextField.text = count.ToString() + "/" + countMax;
    }
    public void Damage(int x)
    {
        if (x > 0)
        {
            if (count < countMax)
            {
                count = count + x; if (count > countMax)
                {
                    count = countMax;
                }

                gauge.value = count;
            }
        }
        else
        {
            if (count < countMin)
            {
                count = countMin;
            }

            gauge.value = count;
        }
        inputTextField.text = count.ToString() + "/" + countMax;
    }
}