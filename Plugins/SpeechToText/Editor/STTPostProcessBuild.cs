using System.IO;
using UnityEngine;
using UnityEditor;
#if UNITY_IOS
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
#endif

namespace SpeechToTextNamespace
{
	[System.Serializable]
	public class Settings
	{
		private const string SAVE_PATH = "ProjectSettings/SpeechToText.json";

		public bool AutomatedSetup = true;
		public string SpeechRecognitionUsageDescription = "Speech recognition will be used for speech-to-text conversion.";
		public string MicrophoneUsageDescription = "Microphone will be used with speech recognition.";

		private static Settings m_instance = null;
		public static Settings Instance
		{
			get
			{
				if( m_instance == null )
				{
					try
					{
						if( File.Exists( SAVE_PATH ) )
							m_instance = JsonUtility.FromJson<Settings>( File.ReadAllText( SAVE_PATH ) );
						else
							m_instance = new Settings();
					}
					catch( System.Exception e )
					{
						Debug.LogException( e );
						m_instance = new Settings();
					}
				}

				return m_instance;
			}
		}

		public void Save()
		{
			File.WriteAllText( SAVE_PATH, JsonUtility.ToJson( this, true ) );
		}

#if UNITY_2018_3_OR_NEWER
		[SettingsProvider]
		public static SettingsProvider CreatePreferencesGUI()
		{
			return new SettingsProvider( "Project/yasirkula/Speech to Text", SettingsScope.Project )
			{
				guiHandler = ( searchContext ) => PreferencesGUI(),
				keywords = new System.Collections.Generic.HashSet<string>() { "Speech", "Text", "Android", "iOS" }
			};
		}
#endif

#if !UNITY_2018_3_OR_NEWER
		[PreferenceItem( "Speech to Text" )]
#endif
		public static void PreferencesGUI()
		{
			EditorGUI.BeginChangeCheck();

			Instance.AutomatedSetup = EditorGUILayout.Toggle( "Automated Setup", Instance.AutomatedSetup );

			EditorGUI.BeginDisabledGroup( !Instance.AutomatedSetup );
			Instance.SpeechRecognitionUsageDescription = EditorGUILayout.DelayedTextField( "Speech Recognition Usage Description", Instance.SpeechRecognitionUsageDescription );
			Instance.MicrophoneUsageDescription = EditorGUILayout.DelayedTextField( "Microphone Usage Description", Instance.MicrophoneUsageDescription );
			EditorGUI.EndDisabledGroup();

			if( EditorGUI.EndChangeCheck() )
				Instance.Save();
		}
	}

	public class STTPostProcessBuild
	{
#if UNITY_IOS
		[PostProcessBuild]
		public static void OnPostprocessBuild( BuildTarget target, string buildPath )
		{
			if( !Settings.Instance.AutomatedSetup )
				return;

			if( target == BuildTarget.iOS )
			{
				string pbxProjectPath = PBXProject.GetPBXProjectPath( buildPath );
				string plistPath = Path.Combine( buildPath, "Info.plist" );

				PBXProject pbxProject = new PBXProject();
				pbxProject.ReadFromFile( pbxProjectPath );

#if UNITY_2019_3_OR_NEWER
				string targetGUID = pbxProject.GetUnityFrameworkTargetGuid();
#else
				string targetGUID = pbxProject.TargetGuidByName( PBXProject.GetUnityTargetName() );
#endif

				pbxProject.AddBuildProperty( targetGUID, "OTHER_LDFLAGS", "-weak_framework Speech" );
				pbxProject.AddBuildProperty( targetGUID, "OTHER_LDFLAGS", "-weak_framework Accelerate" );

				pbxProject.RemoveFrameworkFromProject( targetGUID, "Speech.framework" );
				pbxProject.RemoveFrameworkFromProject( targetGUID, "Accelerate.framework" );

				File.WriteAllText( pbxProjectPath, pbxProject.WriteToString() );

				PlistDocument plist = new PlistDocument();
				plist.ReadFromString( File.ReadAllText( plistPath ) );

				PlistElementDict rootDict = plist.root;
				if( !string.IsNullOrEmpty( Settings.Instance.SpeechRecognitionUsageDescription ) )
					rootDict.SetString( "NSSpeechRecognitionUsageDescription", Settings.Instance.SpeechRecognitionUsageDescription );
				if( !string.IsNullOrEmpty( Settings.Instance.MicrophoneUsageDescription ) )
					rootDict.SetString( "NSMicrophoneUsageDescription", Settings.Instance.MicrophoneUsageDescription );

				File.WriteAllText( plistPath, plist.WriteToString() );
			}
		}
#endif
	}
}