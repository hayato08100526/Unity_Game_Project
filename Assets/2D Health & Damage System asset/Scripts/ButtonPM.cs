using UnityEngine;

public class ButtonPM : MonoBehaviour
{
    public void ButtonPlus()
    {
        GameObject.Find("Score").GetComponent<ScoreGauge>().Damage(1);
    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void ButtonMinus()
    {
        GameObject.Find("Score").GetComponent<ScoreGauge>().Damage(-1);
    }
}
    
