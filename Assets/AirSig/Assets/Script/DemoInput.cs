using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

using AirSig;

public class DemoInput : MonoBehaviour {

	public AirSigManager mAirSigManager;

	private Text mText;

	private string mLastMatchResult;

	// Use this for initialization
	void Start () {

		Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

		// Register callback for detecting a gesture
		mAirSigManager.onGestureTriggered += new AirSigManager.OnGestureTriggered(OnGestureTriggered);

		// Register callback for identification result
		mAirSigManager.onCommonGestureMatch += new AirSigManager.OnCommonGestureMatch(OnCommonGestureMatch);

		// Set AirSig engine's identification mode. Currently, only identifying of predefined gesture
		// is available. More modes will be available in the future.
		mAirSigManager.setMode(AirSigManager.Mode.IdentifyCommon);

		// Set AirSig engine' target for identification. Only Heart, C and Down are available now and
		// more will be made available in the future.
		mAirSigManager.setTarget(
			new List<int>() {
				(int)AirSigManager.CommonGesture.Heart,
				(int)AirSigManager.CommonGesture.C,
				(int)AirSigManager.CommonGesture.Down});

		mText = GetComponent<Text> ();
	}
	
	// Update is called once per frame
	void Update () {

		// Update the identification result on the screen
		if(null != mLastMatchResult) {
			mText.text = mLastMatchResult;
			mLastMatchResult = null;
		}
	}

	/**
	 *	Identification result callback
	 *
	 *  @param gestureId A serial number for this identication. This is the same number for OnGestureTriggered
	 * 	callback
	 * 
	 *	@param gesture The matched gesture. Currently, it can be one of following:
	 *  AirSigManager.CommonGesture.None, AirSigManager.CommonGesture.Heart, AirSigManager.CommonGesture.C or
	 *	AirSigManager.CommonGesture.Down
	 *	However, this depends on the setTarget parameter. If a gesture was not in the setTarget parameter, then
	 *	it is not possible to be the matched gesture.
	 *
	 *	@param score The score of the matching gesture. Scores under 0.9 is considered a no match.
	 */
	void OnCommonGestureMatch(long gestureId, AirSigManager.CommonGesture gesture, float score) {
		if(gesture == AirSigManager.CommonGesture.None || score <= 1.0f) {
			mLastMatchResult = "No match!";
		}
		else {
			mLastMatchResult = string.Format("{0} (Score: {1})", gesture, score);
		}
	}

	void OnGestureTriggered(long gestureId) {
	}
}
