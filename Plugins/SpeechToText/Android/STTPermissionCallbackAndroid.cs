#if UNITY_EDITOR || UNITY_ANDROID
using UnityEngine;

namespace SpeechToTextNamespace
{
	public class STTPermissionCallbackAndroid : AndroidJavaProxy
	{
		private readonly SpeechToText.PermissionCallback callback;
		private readonly STTCallbackHelper callbackHelper;

		public STTPermissionCallbackAndroid( SpeechToText.PermissionCallback callback ) : base( "com.yasirkula.unity.SpeechToTextPermissionReceiver" )
		{
			this.callback = callback;
			callbackHelper = STTCallbackHelper.Create( true );
		}

		[UnityEngine.Scripting.Preserve]
		public void OnPermissionResult( int result )
		{
			callbackHelper.CallOnMainThread( () => callback( (SpeechToText.Permission) result ) );
		}
	}
}
#endif