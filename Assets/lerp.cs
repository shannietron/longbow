using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class lerp : MonoBehaviour {

	public GameObject weeble;
	private Vector3 startPos;
	private Vector3 endPos;
	public float distance = 30f;
	public float lerpTime = 5f;
	private float currentLerpTime;


	// Use this for initialization
	void Start () {
		startPos = transform.position;
		endPos = transform.position + Vector3.forward * distance;


		
	}
	
	// Update is called once per frame
	void Update () {
		if (Input.GetKeyDown (KeyCode.Space)) {
			currentLerpTime = 0f;
		}

		currentLerpTime += Time.deltaTime;
		if (currentLerpTime > lerpTime) {
			currentLerpTime	= 0f;
		}

		float perc = currentLerpTime / lerpTime;
		transform.position = Vector3.Lerp(startPos, endPos, perc);


	}
}
