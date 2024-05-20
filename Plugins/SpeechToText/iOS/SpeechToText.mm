#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <Accelerate/Accelerate.h>
#import <Speech/Speech.h>

@interface USpeechToText:NSObject
+ (int)initialize:(NSString *)language;
+ (int)start:(BOOL)useFreeFormLanguageModel preferOfflineRecognition:(BOOL)preferOfflineRecognition;
+ (void)stop;
+ (void)cancel:(BOOL)isCanceledByUser;
+ (int)isLanguageSupported:(NSString *)language;
+ (int)isServiceAvailable:(BOOL)preferOfflineRecognition;
+ (int)isBusy;
+ (float)getAudioRmsdB;
+ (int)checkPermission;
+ (int)requestPermission;
+ (void)openSettings;
@end

// Credit: https://developer.apple.com/documentation/speech/recognizing_speech_in_live_audio?language=objc
@implementation USpeechToText

static NSString *speechRecognizerLanguage;
static SFSpeechRecognizer *speechRecognizer;
static SFSpeechAudioBufferRecognitionRequest *recognitionRequest;
static SFSpeechRecognitionTask *recognitionTask;
static int recognitionTaskErrorCode;
static NSTimer *recognitionTimeoutTimer;
static AVAudioEngine *audioEngine;
static float audioRmsdB;

+ (int)initialize:(NSString *)language
{
	if( @available(iOS 10.0, *) )
	{
		if( [self isBusy] == 1 )
			return 0;
	}
	else
		return 0;
	
	if( speechRecognizerLanguage == nil || ![speechRecognizerLanguage isEqualToString:language] )
	{
		speechRecognizerLanguage = language;
		
		[self cancel:NO];
		
		if( language == nil || [language length] == 0 )
			speechRecognizer = [[SFSpeechRecognizer alloc] init];
		else
			speechRecognizer = [[SFSpeechRecognizer alloc] initWithLocale:[NSLocale localeWithLocaleIdentifier:language]];
	}
	
	return ( speechRecognizer != nil ) ? 1 : 0;
}

+ (int)start:(BOOL)useFreeFormLanguageModel preferOfflineRecognition:(BOOL)preferOfflineRecognition
{
	if( [self isServiceAvailable:preferOfflineRecognition] == 0 || [self isBusy] == 1 || [self requestPermission] != 1 )
		return 0;
	
	// Cancel the previous task if it's running
	[self cancel:NO];
	
	// Cache the current AVAudioSession settings so that they can be restored after the microphone session
	AVAudioSessionCategory unityAudioSessionCategory = [[AVAudioSession sharedInstance] category];
	NSUInteger unityAudioSessionCategoryOptions = [[AVAudioSession sharedInstance] categoryOptions];
	AVAudioSessionMode unityAudioSessionMode = [[AVAudioSession sharedInstance] mode];
	
	AVAudioSession *audioSession = [AVAudioSession sharedInstance];
	[audioSession setCategory:AVAudioSessionCategoryRecord mode:AVAudioSessionModeMeasurement options:AVAudioSessionCategoryOptionDuckOthers error:nil];
	[audioSession setActive:YES withOptions:AVAudioSessionSetActiveOptionNotifyOthersOnDeactivation error:nil];
	
	if( audioEngine == nil )
		audioEngine = [[AVAudioEngine alloc] init];
	
	AVAudioInputNode *inputNode = audioEngine.inputNode;
	if( inputNode == nil )
	{
		NSLog( @"Couldn't get AVAudioInputNode for speech recognition" );
		return 0;
	}
	
	audioRmsdB = 0;
	
	recognitionRequest = [[SFSpeechAudioBufferRecognitionRequest alloc] init];
	if( recognitionRequest == nil )
	{
		NSLog( @"Couldn't create an instance of SFSpeechAudioBufferRecognitionRequest for speech recognition" );
		return 0;
	}
	
	speechRecognizer.defaultTaskHint = useFreeFormLanguageModel ? SFSpeechRecognitionTaskHintDictation : SFSpeechRecognitionTaskHintSearch;
	recognitionRequest.shouldReportPartialResults = YES;
#if __IPHONE_OS_VERSION_MAX_ALLOWED >= 130000
	if( @available(iOS 13.0, *) )
	{
		if( preferOfflineRecognition )
			recognitionRequest.requiresOnDeviceRecognition = YES;
	}
#endif
	
	recognitionTaskErrorCode = 5;
	recognitionTask = [speechRecognizer recognitionTaskWithRequest:recognitionRequest resultHandler:^( SFSpeechRecognitionResult *result, NSError *error )
	{
		BOOL isFinal = NO;
		if( result != nil )
		{
			isFinal = result.isFinal;
			UnitySendMessage( "STTInteractionCallbackiOS", isFinal ? "OnResultReceived" : "OnPartialResultReceived", [self getCString:result.bestTranscription.formattedString] );
		}
		
		if( recognitionTimeoutTimer != nil )
		{
			[recognitionTimeoutTimer invalidate];
			recognitionTimeoutTimer = nil;
		}
		
		if( error != nil || isFinal )
		{
			if( error != nil )
			{
				NSLog( @"Error during speech recognition: %@", error );
				
				if( !isFinal )
					UnitySendMessage( "STTInteractionCallbackiOS", "OnError", [self getCString:[NSString stringWithFormat:@"%d", recognitionTaskErrorCode]] );
			}
			
			[audioEngine stop];
			[inputNode removeTapOnBus:0];
			
			recognitionRequest = nil;
			recognitionTask = nil;
			
			// Try restoring AVAudioSession settings back to their initial values
			NSError *error = nil;
			if( ![[AVAudioSession sharedInstance] setCategory:unityAudioSessionCategory mode:unityAudioSessionMode options:unityAudioSessionCategoryOptions error:&error] )
			{
				NSLog( @"SpeechToText error (1) setting audio session category back to %@ with mode %@ and options %lu: %@", unityAudioSessionCategory, unityAudioSessionMode, (unsigned long) unityAudioSessionCategoryOptions, error );
				
				// It somehow failed. Try restoring AVAudioSession settings back to Unity's default values
				if( ![[AVAudioSession sharedInstance] setCategory:AVAudioSessionCategoryAmbient mode:AVAudioSessionModeDefault options:1 error:&error] )
					NSLog( @"SpeechToText error (2) setting audio session category back to %@ with mode %@ and options %lu: %@", unityAudioSessionCategory, unityAudioSessionMode, (unsigned long) unityAudioSessionCategoryOptions, error );
			}
		}
		else
		{
			// Restart the timeout timer
			recognitionTimeoutTimer = [NSTimer scheduledTimerWithTimeInterval:2.0 target:self selector:@selector(onSpeechTimedOut:) userInfo:nil repeats:NO];
		}
	}];
	
	[inputNode installTapOnBus:0 bufferSize:1024 format:[inputNode outputFormatForBus:0] block:^( AVAudioPCMBuffer *buffer, AVAudioTime *when )
	{
		if( [buffer floatChannelData] != nil && buffer.format.channelCount > 0 )
		{
			float voiceLevel = 0.0;
			vDSP_rmsqv( (float*) buffer.floatChannelData[0], 1, &voiceLevel, vDSP_Length( buffer.frameLength ) );
			audioRmsdB = 10 * log10f( voiceLevel ) + 160; // Convert voice level to dB in range [0, 160]
		}
		else
			audioRmsdB = 0;
		
		[recognitionRequest appendAudioPCMBuffer:buffer];
	}];
	
	NSError *audioEngineError;
	[audioEngine prepare];
	if( ![audioEngine startAndReturnError:&audioEngineError] )
	{
		if( audioEngineError != nil )
			NSLog( @"Couldn't start AudioEngine for speech recognition: %@", audioEngineError );
		else
			NSLog( @"Couldn't start AudioEngine for speech recognition: UnknownError" );
		
		[recognitionTask cancel];
		return 0;
	}
	
	recognitionTimeoutTimer = [NSTimer scheduledTimerWithTimeInterval:5.0 target:self selector:@selector(onSpeechTimedOut:) userInfo:nil repeats:NO];
	
	return 1;
}

+ (void)onSpeechTimedOut:(NSTimer *)timer
{
	recognitionTimeoutTimer = nil;
	recognitionTaskErrorCode = 6;
	
	[self stop];
}

+ (void)stop
{
	if( @available(iOS 10.0, *) )
	{
		if( audioEngine != nil && audioEngine.isRunning )
		{
			[audioEngine stop];
			[recognitionRequest endAudio];
		}
	}
}

+ (void)cancel:(BOOL)isCanceledByUser
{
	if( @available(iOS 10.0, *) )
	{
		if( recognitionTask != nil )
		{
			if( isCanceledByUser )
				recognitionTaskErrorCode = 0;
			
			[recognitionTask cancel];
			recognitionTask = nil;
		}
	}
}

+ (int)isLanguageSupported:(NSString *)language
{
	if( @available(iOS 10.0, *) )
		return [[SFSpeechRecognizer supportedLocales] containsObject:[NSLocale localeWithLocaleIdentifier:language]] ? 1 : 0;
	
	return 0;
}

+ (int)isServiceAvailable:(BOOL)preferOfflineRecognition
{
	if( @available(iOS 10.0, *) )
	{
		if( speechRecognizer != nil && [speechRecognizer isAvailable] )
		{
			if( !preferOfflineRecognition )
				return 1;
#if __IPHONE_OS_VERSION_MAX_ALLOWED >= 130000
			else if( @available(iOS 13.0, *) )
				return [speechRecognizer supportsOnDeviceRecognition] ? 1 : 0;
#endif
		}
	}
	
	return 0;
}

+ (int)isBusy
{
	if( @available(iOS 10.0, *) )
		return ( recognitionRequest != nil ) ? 1 : 0;
	
	return 0;
}

+ (float)getAudioRmsdB
{
	return audioRmsdB;
}

+ (int)checkPermission
{
	if( @available(iOS 10.0, *) )
	{
		int speechRecognitionPermission = [self checkSpeechRecognitionPermission];
		int microphonePermission = [self checkMicrophonePermission];
		if( speechRecognitionPermission == 1 && microphonePermission == 1 )
			return 1;
		else if( speechRecognitionPermission != 0 && microphonePermission != 0 )
			return 2;
	}
	
	return 0;
}

+ (int)checkSpeechRecognitionPermission
{
	SFSpeechRecognizerAuthorizationStatus status = [SFSpeechRecognizer authorizationStatus];
	if( status == SFSpeechRecognizerAuthorizationStatusAuthorized )
		return 1;
	else if( status == SFSpeechRecognizerAuthorizationStatusNotDetermined )
		return 2;
	else
		return 0;
}

+ (int)checkMicrophonePermission
{
#if __IPHONE_OS_VERSION_MAX_ALLOWED >= 170000
	if( @available(iOS 17.0, *) )
	{
		AVAudioApplicationRecordPermission status = [[AVAudioApplication sharedInstance] recordPermission];
		if( status == AVAudioApplicationRecordPermissionGranted )
			return 1;
		else if( status == AVAudioApplicationRecordPermissionUndetermined )
			return 2;
	}
	else
#endif
	{
		AVAudioSessionRecordPermission status = [[AVAudioSession sharedInstance] recordPermission];
		if( status == AVAudioSessionRecordPermissionGranted )
			return 1;
		else if( status == AVAudioSessionRecordPermissionUndetermined )
			return 2;
	}

	return 0;
}

+ (int)requestPermission
{
	int currentPermission = [self checkPermission];
	if( currentPermission != 2 )
	{
		UnitySendMessage( "STTPermissionCallbackiOS", "OnPermissionRequested", [self getCString:[NSString stringWithFormat:@"%d", currentPermission]] );
		return currentPermission;
	}
	
	// Request Speech Recognition permission first
	[SFSpeechRecognizer requestAuthorization:^( SFSpeechRecognizerAuthorizationStatus status )
	{
		// Request Microphone permission immediately afterwards
#if __IPHONE_OS_VERSION_MAX_ALLOWED >= 170000
		// For some reason, requestRecordPermissionWithCompletionHandler function couldn't be found in AVAudioApplication while writing this code. Uncomment when it's fixed by Apple.
		/*if( @available(iOS 17.0, *) )
		{
			[[AVAudioApplication sharedInstance] requestRecordPermissionWithCompletionHandler:^( BOOL granted )
			{
				UnitySendMessage( "STTPermissionCallbackiOS", "OnPermissionRequested", ( granted &&  status == SFSpeechRecognizerAuthorizationStatusAuthorized ) ? "1" : "0" );
			}];
		}
		else*/
#endif
		{
			[[AVAudioSession sharedInstance] requestRecordPermission:^( BOOL granted )
			{
				UnitySendMessage( "STTPermissionCallbackiOS", "OnPermissionRequested", ( granted &&  status == SFSpeechRecognizerAuthorizationStatusAuthorized ) ? "1" : "0" );
			}];
		}
	}];
	
	return -1;
}

#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
+ (void)openSettings
{
#if __IPHONE_OS_VERSION_MAX_ALLOWED >= 100000
	if( @available(iOS 10.0, *) )
		[[UIApplication sharedApplication] openURL:[NSURL URLWithString:UIApplicationOpenSettingsURLString] options:@{} completionHandler:nil];
	else
#endif
		[[UIApplication sharedApplication] openURL:[NSURL URLWithString:UIApplicationOpenSettingsURLString]];
}
#pragma clang diagnostic pop

// Credit: https://stackoverflow.com/a/37052118/2373034
+ (char *)getCString:(NSString *)source
{
	if( source == nil )
		source = @"";
	
	const char *sourceUTF8 = [source UTF8String];
	char *result = (char*) malloc( strlen( sourceUTF8 ) + 1 );
	strcpy( result, sourceUTF8 );
	
	return result;
}

@end

extern "C" int _SpeechToText_Initialize( const char* language )
{
	return [USpeechToText initialize:[NSString stringWithUTF8String:language]];
}

extern "C" int _SpeechToText_Start( int useFreeFormLanguageModel, int preferOfflineRecognition )
{
	return [USpeechToText start:( useFreeFormLanguageModel == 1 ) preferOfflineRecognition:( preferOfflineRecognition == 1 )];
}

extern "C" void _SpeechToText_Stop()
{
	[USpeechToText stop];
}

extern "C" void _SpeechToText_Cancel()
{
	[USpeechToText cancel:YES];
}

extern "C" int _SpeechToText_IsLanguageSupported( const char* language )
{
	return [USpeechToText isLanguageSupported:[NSString stringWithUTF8String:language]];
}

extern "C" int _SpeechToText_IsServiceAvailable( int preferOfflineRecognition )
{
	return [USpeechToText isServiceAvailable:( preferOfflineRecognition == 1 )];
}

extern "C" int _SpeechToText_IsBusy()
{
	return [USpeechToText isBusy];
}

extern "C" float _SpeechToText_GetAudioRmsdB()
{
	return [USpeechToText getAudioRmsdB];
}

extern "C" int _SpeechToText_CheckPermission()
{
	return [USpeechToText checkPermission];
}

extern "C" void _SpeechToText_RequestPermission()
{
	[USpeechToText requestPermission];
}

extern "C" void _SpeechToText_OpenSettings()
{
	[USpeechToText openSettings];
}