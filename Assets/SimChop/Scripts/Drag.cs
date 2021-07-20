using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Drag : MonoBehaviour
{
    private Vector3 mOff;
    private float objZ;
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void OnMouseDown(){
      objZ = Camera.main.WorldToScreenPoint(gameObject.transform.position).z;

      mOff = gameObject.transform.position - GetMouseWorldPos();

    }
    void Update()
    {


    }
    Vector3 GetMouseWorldPos(){
      Vector3 mousePoint = Input.mousePosition;

      mousePoint.z = objZ;
      return Camera.main.ScreenToWorldPoint(mousePoint);
    }
    void OnMouseDrag(){
      transform.position = new Vector3(GetMouseWorldPos().x + mOff.x, transform.position.y, GetMouseWorldPos().z + mOff.z);

    }
}
