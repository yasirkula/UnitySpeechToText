#if UNITY_EDITOR || UNITY_ANDROID
using System.Threading;
using UnityEngine;

namespace SpeechToTextNamespace
{
	public class STTPermissionCallbackAndroid : AndroidJavaProxy
	{
		private readonly object threadLock;
		public int Result { get; private set; }

		public STTPermissionCallbackAndroid( object threadLock ) : base( "com.yasirkula.unity.SpeechToTextPermissionReceiver" )
		{
			Result = -1;
			this.threadLock = threadLock;
		}

		[UnityEngine.Scripting.Preserve]
		public void OnPermissionResult( int result )
		{
			Result = result;

			lock( threadLock )
			{
				Monitor.Pulse( threadLock );
			}
		}
	}

	public class STTPermissionCallbackAsyncAndroid : AndroidJavaProxy
	{
		private readonly SpeechToText.PermissionCallback callback;
		private readonly STTCallbackHelper callbackHelper;

		public STTPermissionCallbackAsyncAndroid( SpeechToText.PermissionCallback callback ) : base( "com.yasirkula.unity.SpeechToTextPermissionReceiver" )
		{
			this.callback = callback;
			callbackHelper = new GameObject( "STTCallbackHelper" ).AddComponent<STTCallbackHelper>().AutoDestroy();
		}

		[UnityEngine.Scripting.Preserve]
		public void OnPermissionResult( int result )
		{
			callbackHelper.CallOnMainThread( () => callback( (SpeechToText.Permission) result ) );
		}
	}
}
#endif