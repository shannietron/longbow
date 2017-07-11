using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using UnityEngine;

namespace AirSig {
public class AirSigManager : MonoBehaviour {

	/// Enable debug logging
	/*public*/private static bool DEBUG_LOG_ENABLED = false;

	// Default interval for sensor sampling rate.
	// Increasing this makes less sample for a fixed period of time
	/*public*/private const float MIN_INTERVAL = 16f;

	/// Threshold score for a common gesture to be considered pass
	/*public*/private const int COMMON_MISTOUCH_THRESHOLD = 30;	//0.5sec (1 sec is about 60)
	/*public*/private const float COMMON_PASS_THRESHOLD = 0.9f;
	/*public*/private const float SMART_TRAIN_PASS_THRESHOLD = 1f;

	/// Threshold for engine
	/*public*/private const float THRESHOLD_TRAINING_MATCH_THRESHOLD = 0.98f;
	/*public*/private const float THRESHOLD_VERIFY_MATCH_THRESHOLD = 0.98f;
	/*public*/private const float THRESHOLD_IS_TWO_GESTURE_SIMILAR = 1.0f;

	/// Available common gestures
	public enum CommonGesture : int {
		None    = 0,
		_Start  = 1000,
		Heart	= 1001, 
		C 		= 1003, 
		Down 	= 1006, 
		_End    = 1008
	};

	/// All common gesture
	public static readonly List<int> ALL_COMMON_GESTURE = new List<int>() {
		(int)CommonGesture.Heart, (int)CommonGesture.C, (int)CommonGesture.Down
	};

	/// Identification/Training mode
	public enum Mode : int { 
		None = 0x00,			// will not perform any identification`
		IdentifyCommon = 0x01,	// will perform common gesture identification
	};

	/// Errors used in OnUserGestureTrained callback
	/*public*/private class Error {

		public static readonly int SIGN_TOO_FEW_WORD = -204;
		public static readonly int SIGN_WITH_MISTOUCH = -200;
		
		public int code;
		public String message;

		public Error(int errorCode, String message) {
			this.code = errorCode;
			this.message = message;
		}
	}

	/// Strength used in OnUserGestureTrained callback
	/*public*/private enum SecurityLevel : int {
		None = 0,
		Very_Poor = 1,
		Poor = 2,
		Normal = 3,
		High = 4,
		Very_High = 5
	}

	// Mode of operation
	private Mode mCurrentMode = Mode.None;

	// Current target for 
	private List<int> mCurrentTarget = new List<int>();

	// Keep the current instance
	private static AirSigManager sInstance;

	/// Event handler for receiving common gesture matching result
	public delegate void OnCommonGestureMatch(long gestureId, CommonGesture gesture, float score);
	public event OnCommonGestureMatch onCommonGestureMatch;

	/// Event handler for receiving triggering of a gesture
	public delegate void OnGestureTriggered(long gestureId);
	public event OnGestureTriggered onGestureTriggered;

	static Thread mainThread = Thread.CurrentThread;

	/// Set the identification mode for the next incoming gesture
	public void setMode(Mode mode) {
		if(mCurrentMode == mode) {
			if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][setMode] new mode equals to the existing mode so nothing will change...");
			return;
		}

		mCurrentMode = mode;

		ContinuousRecognizeEnabled = true;

		mTrainFailCount = 0;

		// clear all incomplete training
		mTrainingProgressGestures.Clear();
	}

	/// Set the identification target for the next incoming gesture
	public void setTarget(List<int> target) {
		if(mCurrentTarget.SequenceEqual(target)) {
			if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][setTarget] new targets equal to the existing targets so nothing will change...");
			return;
		}

		mCurrentTarget = target;

		mTrainFailCount = 0;

		// clear all incomplete training
		mTrainingProgressGestures.Clear();
	}

	/// Reset smart training data
	/*public*/private void ResetSmartTrain() {
	}

	// Use to get ID of a gesture
	private static readonly DateTime InitTime = DateTime.UtcNow;
	private static long GetCurrentGestureID() {
		return (long) (DateTime.UtcNow - InitTime).TotalMilliseconds;
	}

	// Train fail accumlative Count
	private int mTrainFailCount = 0;

	// security level too low count
	private static int mSecurityTooLowCount = 0;

	// specify algorithm used within
	private int mAlgorithmVer = 0;

	// New training API
	private const int TRAINING_STEP = 5;
	private float[] mTrainingProgress = new float[TRAINING_STEP] {
		0.2f, 0.4f, 0.6f, 0.8f, 1.0f
	};
	private List<AndroidJavaObject> mTrainingProgressGestures = new List<AndroidJavaObject>();

	// Cache for recent used sensor data
	private const int CACHE_SIZE = 10;
	private SortedDictionary<long, AndroidJavaObject> mCache = new SortedDictionary<long, AndroidJavaObject>();

	// Data structure for saving current training status
	// private TrainData mTrainData = new TrainData();
	private bool mHasTrainDataChanged = false;

	// To handle short signature when setup 
	private static int mFirstTrainDataSize = 0;
	private const float TRAIN_DATA_THRESHOLD_RATIO = 0.65f;

	// Smart training sensor data of a same target
	private SortedList<float, AndroidJavaObject> mSmartTrainCache = new SortedList<float, AndroidJavaObject>();
	private class SmartTrainActionBundle {
		public List<AndroidJavaObject> cache;
		public int targetIndex;
		public int nextIndex;
		public float progress;
		public SmartTrainActionBundle(int targetIndex, List<AndroidJavaObject> cache) {
			this.targetIndex = targetIndex;
			this.cache = cache;
			this.nextIndex = cache.Count - 1; // starting from the last element
			this.progress = 0f;
		}
	}

	// For storing smart identify result
	private class IdentifyActionBundle {
		public long id;
		public int basedIndex;
		public int matchIndex;
		public string type;
		public float score;
		public AndroidJavaObject sensorData;
		public IdentifyActionBundle(long gestureId, int basedIndex, AndroidJavaObject sensorData) {
			this.id = gestureId;
			this.basedIndex = basedIndex;
			this.sensorData = sensorData;
			this.score = 0f;
		}
	}

	// Store all shortcut gesture's error count stat
	private bool mIsGestureStatExist = false;
	private Dictionary<int, ErrorCount> mGestureStat = new Dictionary<int, ErrorCount>();

	// Store all cache of smart training
	private Dictionary<int, List<AndroidJavaObject>> mSmartTrainCacheCollection = new Dictionary<int, List<AndroidJavaObject>>();

	// For internal algorithm picking proirity
	private class ErrorCount {
		public int commonErrCount;
		public int userErrCount;

		public ErrorCount() {
			this.commonErrCount = 0;
			this.userErrCount = 0;
		}

		public bool isCommonErrHigher() {
			return commonErrCount > userErrCount;
		}

		public bool isUserErrHigher() {
			return userErrCount > commonErrCount;
		}
	}

	// AirSig Engine
	private const string licenseKey = "2dgc5evux5kp48lb6pjr8vy5dk4rxh9nyiz1ja6cymezv8054squq6sduz7miut187cplpamgb1ezdtih5v51wfq13nqaqhezag3qtr16jrac6jxrbizskdrjvaq91pcr5vua1jndwvspqb66jt0mk9c2xfl82svkgn6mlwhcrncztzev0prs90hwzcj7cvc0wchylui516b7av0f5t1veizg0mo82zzxqkwjfiy5vqwictrotsargs9tr0fifnjd9izjpu5unmd1uypisvvmvpt08meuo3wraxsuik63mf7r5g9gae9pkcgkemydn3q9aua3dz99gdk99o64pk1opxxuk6xg9qg5kp14hxb4nd889y7ksdwj9f7859y7ivxoligdq2qd7dro";

	private static AndroidJavaObject sASEngineInstance;
	private bool mIsASEngineValidLicense = false;

	// Google VR
	public GvrController mController;

	/// ddcontroller

	private bool appButtonState;
	private bool touchButtonDown;
	public static bool AppButton {
		get {
			return sInstance != null ? sInstance.appButtonState : false;
		}
	}
	public static bool TouchButtonDown {
		get {
			return sInstance != null ? sInstance.touchButtonDown : false;
		}
	}

	// Daydream remote control manager for receving sensor data
	private static AndroidJavaObject sControlManagerInstance;

	private float mNotifyScoreThreshold = -0.5f;

	/// Set and get for continuous recognization using pause interval for break
	public bool ContinuousRecognizeEnabled {
		get {
			return getControlManagerInstance().Call<bool>("isContinuousRecognizeEnabled"); 
		}
		set {
			object[] arglist = new object[1];
			arglist[0] = value;
			getControlManagerInstance().Call("setContinuousRecognizeEnabled", arglist);
		}
	}

	public long PauseInterval {
		get {
			return getControlManagerInstance().Call<long>("getPauseInterval"); 
		}
		set {
			object[] arglist = new object[1];
			arglist[0] = value;
			getControlManagerInstance().Call("setPauseInterval", arglist);
		}
	}

	public int AlgorithmVer {
		set {
			mAlgorithmVer = value;
		}
	}
	
	private Quaternion mLastTouchRotation;
	private Dictionary<int, Quaternion> mRecordTouchRotation = new Dictionary<int, Quaternion>();
	
	/*public*/private void saveLastTouch(int id) {
	}

	/*public*/private void showControllerForId(int id) {
	}
	
	// Receiver for Daydream controller
	private void OnControllerUpdate() {
		appButtonState = GvrController.AppButton;
		touchButtonDown = GvrController.ClickButtonDown;
	}

	private static AndroidJavaObject getControlManagerInstance() {
		if(null == sControlManagerInstance) {
			AndroidJavaClass unityPlayer = new AndroidJavaClass ("com.unity3d.player.UnityPlayer"); 
			AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject> ("currentActivity");

			AndroidJavaClass controlManagerC = new AndroidJavaClass ("com.airsig.dd_control_manager.ControlManager");

			object[] arglist = new object[1];
			arglist [0] = (object)activity;
			sControlManagerInstance = controlManagerC.CallStatic<AndroidJavaObject> ("getInstance", arglist);

			arglist [0] = (object)new ControlListener (sInstance);
			sControlManagerInstance.Call ("setUpdateListener", arglist);
		}
		return sControlManagerInstance;
	}

	private static AndroidJavaObject getEngineInstance() {
		if (null == sASEngineInstance) {
			AndroidJavaClass unityPlayer = new AndroidJavaClass ("com.unity3d.player.UnityPlayer"); 
			AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject> ("currentActivity");

			AndroidJavaClass asEngineC = new AndroidJavaClass ("com.airsig.airsigengmulti.ASEngine");

			AndroidJavaClass asEngineParametersC = new AndroidJavaClass ("com.airsig.airsigengmulti.ASEngine$ASEngineParameters");

			AndroidJavaObject engineParam = asEngineParametersC.GetStatic<AndroidJavaObject> ("Default");
			engineParam.Set<int>("maxFailedTrialsInARow", Int32.MaxValue);
			engineParam.Set<int>("secondsToResetFailedTrialRecord", 0);
			// engineParam.Set<float>("trainingMatchThreshold", THRESHOLD_TRAINING_MATCH_THRESHOLD);
			// engineParam.Set<float>("verifyMatchThreshold", THRESHOLD_VERIFY_MATCH_THRESHOLD);

			object[] arglist = new object[4];  
			arglist [0] = (object)activity;  
			arglist [1] = (object)licenseKey;
			arglist [2] = null;
			arglist [3] = (object)engineParam;
			sASEngineInstance = asEngineC.CallStatic<AndroidJavaObject> ("initSharedInstance", arglist);
		}
		return sASEngineInstance;
	}

	/// Delete a trained target
	/*public*/private void DeleteUserGesture(int targetIndex) {
	}

	private void TrainUserGesture(long id, int targetIndex, AndroidJavaObject sensorData, Action<SmartTrainActionBundle> furtherAction, SmartTrainActionBundle bundle) {

	}

	private void SmartTrainUserGesture2(int target) {
	
	}

	private void SmartTrainGestures(SmartTrainActionBundle bundle) {
	
	}

	private void SmartTrainUserGesture2(SmartTrainActionBundle bundle) {
		
	}

	private void SmartTrainUserGestureAndVerify(IdentifyActionBundle bundle) {
		
	}

	private void SmartIdentifyGesture(long id, AndroidJavaObject sensorData) {
	
	}

	private void SmartCommonIdentifyResult(IdentifyActionBundle bundle) {
	
	}

	private void SmartUserIdentifyResult(IdentifyActionBundle bundle) {
	
	}

	private void IdentifyUserGesture(long id, AndroidJavaObject sensorData, int[] targetIndex, Action<IdentifyActionBundle> furtherAction, IdentifyActionBundle bundle, bool notifyObserver) {
		
	}

	private void IdentifyUserGesture(long id, AndroidJavaObject sensorData, int[] targetIndex) {

	}

	private void IdentifyCommonGesture(long id, float passScore, AndroidJavaObject sensorData, List<int> targetIndex) {
		IdentifyCommonGesture(id, passScore, sensorData, targetIndex, null, null, true);
	}

	DateTime timeStart2;
	private void IdentifyCommonGesture(long id, float passScore, AndroidJavaObject sensorData, List<int> targetIndex, Action<IdentifyActionBundle> furtherAction, IdentifyActionBundle bundle, bool toInvokeCommonObserver) {
		if(targetIndex.Count <= 0) {
			return;
		}
		AndroidJavaClass asGestureC = new AndroidJavaClass ("com.airsig.airsigengmulti.ASEngine$ASGesture");
		AndroidJavaObject heart = asGestureC.GetStatic<AndroidJavaObject> ("HEART");
		AndroidJavaObject s = asGestureC.GetStatic<AndroidJavaObject> ("s");
		AndroidJavaObject c = asGestureC.GetStatic<AndroidJavaObject> ("c");
		AndroidJavaObject up = asGestureC.GetStatic<AndroidJavaObject> ("UP");
		AndroidJavaObject right = asGestureC.GetStatic<AndroidJavaObject> ("RIGHT");
		AndroidJavaObject down = asGestureC.GetStatic<AndroidJavaObject> ("DOWN");
		AndroidJavaObject left = asGestureC.GetStatic<AndroidJavaObject> ("LEFT");

		IEnumerable<int> query = targetIndex.Where(target => target > (int)CommonGesture._Start && target < (int)CommonGesture._End);
		AndroidJavaObject compareList = new AndroidJavaObject("java.util.ArrayList");

		object[] arglist;
		for(int i = 0; i < query.Count(); i ++) {
			switch(query.ElementAt(i)) {
			case (int)CommonGesture.Heart:
				arglist = new object[1];
				arglist [0] = heart;
				compareList.Call<bool>("add", arglist);
				break;
			case (int)CommonGesture.C:
				arglist = new object[1];
				arglist [0] = c;
				compareList.Call<bool>("add", arglist);
				break;
			case (int)CommonGesture.Down:
				arglist = new object[1];
				arglist [0] = down;
				compareList.Call<bool>("add", arglist);
				break;
			}
		}

 		IntPtr methodId = AndroidJNIHelper.GetMethodID(
 			getEngineInstance().GetRawClass(),
 			"multipleRecognizeGesture",
			"(Ljava/util/ArrayList;Ljava/util/ArrayList;Lcom/airsig/airsigengmulti/ASEngine$OnMultipleGestureRecognizingResultListener;)V");
		
 		object[] argsToConv = new object[3];
		argsToConv[0] = compareList;
 		argsToConv[1] = sensorData.GetRawObject();
 		argsToConv[2] = new OnMultipleGestureRecognizingResultListener(id, passScore, furtherAction, bundle, toInvokeCommonObserver);
 		jvalue[] methodArgs = AndroidJNIHelper.CreateJNIArgArray(argsToConv);
		methodArgs[0].l = compareList.GetRawObject();
 		methodArgs[1].l = sensorData.GetRawObject();
 
 		timeStart2 = DateTime.UtcNow;
		AndroidJNI.CallVoidMethod(getEngineInstance().GetRawObject(), methodId, methodArgs);

	}

	class OnMultipleGestureRecognizingResultListener : AndroidJavaProxy {

		private long mId;
		private Action<IdentifyActionBundle> mFurtherAction;
		private IdentifyActionBundle mBundle;
		private float mPassScore;
		private bool mToInvokeCommonObserver;

		public OnMultipleGestureRecognizingResultListener(long id, float passScore, Action<IdentifyActionBundle> furtherAction, IdentifyActionBundle bundle, bool toInvokeCommonObserver)
			: base("com.airsig.airsigengmulti.ASEngine$OnMultipleGestureRecognizingResultListener") {
			mId = id;
			mFurtherAction = furtherAction;
			mBundle = bundle;
			mPassScore = passScore;
			mToInvokeCommonObserver = toInvokeCommonObserver;
		}

		void onResult(AndroidJavaObject asGesture, float v, AndroidJavaObject asError) {
			if(DEBUG_LOG_ENABLED) {
				Debug.Log(
					string.Format("[AirSigManager][IdentifyCommon] id:{0}, gesture:{1}, score:{2}, error:{3}",
						mId,
						asGesture == null ? "null" : asGesture.Call<string>("name"),
						v,
						asError == null ? "null" : asError.Get<string>("message"))
				);

			}

			String gestureName = asGesture.Call<string>("name");

			// TODO: refactor this matching
			CommonGesture gesture = 0;
			if(String.Compare(gestureName, "HEART", true) == 0) {
				gesture = CommonGesture.Heart;
			}
			else if(String.Compare(gestureName, "c", true) == 0) {
				gesture = CommonGesture.C;
			}
			else if(String.Compare(gestureName, "DOWN", true) == 0) {
				gesture = CommonGesture.Down;
			}

			if(null != mFurtherAction && null != mBundle) {
				if(v >= mPassScore) {
					mBundle.matchIndex = (int)gesture;
				}
				else {
					mBundle.matchIndex = (int)CommonGesture.None;
				}
				mBundle.score = v;
				mBundle.type = "common";
				mFurtherAction(mBundle);
			}

			if(mToInvokeCommonObserver) {
				if(null != sInstance.onCommonGestureMatch) {
					if(v >= mPassScore) {
						sInstance.onCommonGestureMatch(mId, gesture, v);
					}
					else {
						sInstance.onCommonGestureMatch(mId, CommonGesture.None, v);
					}
				}
				else {
					if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager][IdentifyCommon] Listener for onCommonGestureMatch does not exist!");
				}
			}
		}
	}

	class ControlListener : AndroidJavaProxy {

		private AirSigManager manager;

		public ControlListener(AirSigManager manager) : base("com.airsig.dd_control_manager.ControlListener") {
			this.manager = manager;
		}

		void OnSensorDataRecorded(AndroidJavaObject data, float length) {
			long id = GetCurrentGestureID();
			if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] OnSensorDataRecorded - id:" + id + ", mode: " + manager.mCurrentMode + ", length: " + length);
			manager.AddToCache(id, data);
			manager.PerformActionWithGesture(manager.mCurrentMode, id, data);
			if(null != sInstance.onGestureTriggered) {
				sInstance.onGestureTriggered(id);
			}
		}
	}

	void AddToCache(long id, AndroidJavaObject sensorData) {
		while(mCache.Count >= CACHE_SIZE) {
			KeyValuePair<long, AndroidJavaObject> instance = mCache.First();
			mCache.Remove(instance.Key);
		}
		mCache.Add(id, sensorData);
	}

	AndroidJavaObject GetFromCache(long id) {
		if(mCache.ContainsKey(id)) {
			return mCache[id];
		}
		return null;
	}

	KeyValuePair<long, AndroidJavaObject> GetLastFromCache() {
		if(mCache.Count > 0) {
			return mCache.Last();
		}
		return default(KeyValuePair<long, AndroidJavaObject>);
	}

	void PerformActionWithGesture(Mode action, long gestureId, AndroidJavaObject sensorData) {
		if(null == sensorData) {
			if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] Sensor data is not available!");
			return;
		}

		if((action & AirSigManager.Mode.IdentifyCommon) > 0) {
			if(DEBUG_LOG_ENABLED) Debug.Log("[AirSigManager] IdentifyCommonGesture for " + gestureId + "...");

			IdentifyCommonGesture (gestureId, COMMON_PASS_THRESHOLD, sensorData, mCurrentTarget);
		}

	}

	private Nullable<int> IdentifyNoProcess(long id, AndroidJavaObject floatArray, int[] targets) {

		return null;
	}

	private bool TrainAndNoProcess(AndroidJavaObject floatArray) {

		return true;
	}

	// Callback for filtering bad gesture before adding to smart train database
	private void SmartTrainFilterData(IdentifyActionBundle bundle) {
	}

	public void PerformActionWithGesture(Mode action, long gestureId) {
		PerformActionWithGesture(action, gestureId, GetFromCache(gestureId));
	}

	void Update() {
	}

	void Awake() {

		if (sInstance != null) {
			Debug.LogError("More than one AirSigManager instance was found in your scene. "
				+ "Ensure that there is only one AirSigManager GameObject.");
			this.enabled = false;
			return;
		}
		sInstance = this;

		// Init AirSig engine
		AirSigManager.getEngineInstance ();

		// Register Gvr update
		mController.OnControllerUpdate += new GvrController.OnControllerUpdateEvent (OnControllerUpdate);

		// Init Daydream control
		AirSigManager.getControlManagerInstance ();
		PauseInterval = 150;
		Debug.Log("[AirSigManager] Continuous recognize pause interval: " + PauseInterval);

		mCache.Clear();

		Load();

		/// set Algorithm ver
		AlgorithmVer = 1;	//Customized Gesture 1:Enable, 0:Disable
	}

	void OnDestroy() {
		sInstance = null;
		mController.OnControllerUpdate -= new GvrController.OnControllerUpdateEvent (OnControllerUpdate);
	}

	void Load() {
	}

	void Save() {
	}

}
}