#if UNITY_EDITOR || UNITY_IOS
using UnityEngine;

namespace SpeechToTextNamespace
{
	public class STTPermissionCallbackiOS : MonoBehaviour
	{
		private static STTPermissionCallbackiOS instance;
		private SpeechToText.PermissionCallback callback;

		public static void Initialize( SpeechToText.PermissionCallback callback )
		{
			if( instance == null )
			{
				instance = new GameObject( "STTPermissionCallbackiOS" ).AddComponent<STTPermissionCallbackiOS>();
				DontDestroyOnLoad( instance.gameObject );
			}
			else if( instance.callback != null )
				instance.callback( SpeechToText.Permission.ShouldAsk );

			instance.callback = callback;
		}

		[UnityEngine.Scripting.Preserve]
		public void OnPermissionRequested( string message )
		{
			SpeechToText.PermissionCallback _callback = callback;
			callback = null;

			if( _callback != null )
				_callback( (SpeechToText.Permission) int.Parse( message ) );
		}
	}
}
#endif