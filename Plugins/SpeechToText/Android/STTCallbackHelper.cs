#if UNITY_EDITOR || UNITY_ANDROID
using UnityEngine;

namespace SpeechToTextNamespace
{
	public class STTCallbackHelper : MonoBehaviour
	{
		private bool autoDestroy;
		private System.Action mainThreadAction = null;

		private void Awake()
		{
			DontDestroyOnLoad( gameObject );
		}

		private void Update()
		{
			if( mainThreadAction != null )
			{
				try
				{
					lock( this )
					{
						System.Action temp = mainThreadAction;
						mainThreadAction = null;
						temp();
					}
				}
				finally
				{
					if( autoDestroy )
						Destroy( gameObject );
				}
			}
		}

		public STTCallbackHelper AutoDestroy()
		{
			autoDestroy = true;
			return this;
		}

		public void CallOnMainThread( System.Action function )
		{
			lock( this )
			{
				mainThreadAction += function;
			}
		}
	}
}
#endif