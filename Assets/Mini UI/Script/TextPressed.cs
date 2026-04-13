using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class TextPressed : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField]
    private RectTransform myGameObject;
    private Vector2 vector2;
    public float UnitsToMove;

    void Start()
    {
        vector2 = myGameObject.GetComponent<RectTransform>().anchoredPosition;
    }

    public void moveDown() { }

    public void OnPointerDown(PointerEventData eventData)
    {
        myGameObject.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, UnitsToMove);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        myGameObject.GetComponent<RectTransform>().anchoredPosition = new Vector2(
            vector2.x,
            vector2.y
        );
    }
}
