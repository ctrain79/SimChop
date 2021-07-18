using UnityEngine;

public class Flow : MonoBehaviour
{
	void OnTriggerStay(Collider c)
	{
		c.gameObject.GetComponent<Rigidbody>().AddForce(Vector3.right*2, ForceMode.Impulse);
	}
}
