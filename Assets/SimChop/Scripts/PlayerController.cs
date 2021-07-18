using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
	private CharacterController controller;
	
	// Start is called before the first frame update
	void Start()
	{
		controller = GetComponent<CharacterController>();
	}

	// Update is called once per frame
	void Update()
	{
		Vector3 direction = 
			transform.right*Input.GetAxis("Horizontal") +
			1.5f*transform.forward*Input.GetAxis("Vertical");
		
		controller.Move(direction * Time.deltaTime * 3.0f);
		
		if (direction != Vector3.zero)
		{
			transform.forward = Vector3.Lerp(transform.forward, direction, 5*Time.deltaTime);
		}
		
	}
}
