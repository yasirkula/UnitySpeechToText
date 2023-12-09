#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <Accelerate/Accelerate.h>
#import <Speech/Speech.h>

#define CHECK_IOS_VERSION( version )  ([[[UIDevice currentDevice] systemVersion] compare:version options:NSNumericSearch] != NSOrderedAscending)

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
+ (int)requestPermission:(BOOL)asyncMode;
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
	if( !CHECK_IOS_VERSION( @"10.0" ) || [self isBusy] == 1 )
		return 0;
	
	if( speechRecognizerLanguage == nil || ![speechRecognizerLanguage isEqualToString:language] )
	{
		speechRecognizerLanguage = language;
		
		[self cancel:NO];
		
		if( language == nil || [language length] == 0 )
			speechRecognizer = [[SFSpeechRecognizer alloc] init];
		else
			speechRecognizer = [[SFSpeechRecognizer alloc] initWithLocale:[NSLocale localeWithLocaleIdentifier:[language stringByReplacingOccurrencesOfString:@"-" withString:@"_"]]];
	}
	
	return ( speechRecognizer != nil ) ? 1 : 0;
}

+ (int)start:(BOOL)useFreeFormLanguageModel preferOfflineRecognition:(BOOL)preferOfflineRecognition
{
	if( [self isServiceAvailable:preferOfflineRecognition] == 0 || [self isBusy] == 1 || [self requestPermission:YES] != 1 )
		return 0;
	
	// Cancel the previous task if it's running
	[self cancel:NO];
	
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
	if( preferOfflineRecognition )
		recognitionRequest.requiresOnDeviceRecognition = YES;
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
	if( CHECK_IOS_VERSION( @"10.0" ) && audioEngine != nil && audioEngine.isRunning )
	{
		[audioEngine stop];
		[recognitionRequest endAudio];
	}
}

+ (void)cancel:(BOOL)isCanceledByUser
{
	if( CHECK_IOS_VERSION( @"10.0" ) && recognitionTask != nil )
	{
		if( isCanceledByUser )
			recognitionTaskErrorCode = 0;
		
		[recognitionTask cancel];
		recognitionTask = nil;
	}
}

+ (int)isLanguageSupported:(NSString *)language
{
	return ( CHECK_IOS_VERSION( @"10.0" ) && [[SFSpeechRecognizer supportedLocales] containsObject:[NSLocale localeWithLocaleIdentifier:[language stringByReplacingOccurrencesOfString:@"-" withString:@"_"]]] ) ? 1 : 0;
}

+ (int)isServiceAvailable:(BOOL)preferOfflineRecognition
{
	if( CHECK_IOS_VERSION( @"10.0" ) && speechRecognizer != nil && [speechRecognizer isAvailable] )
	{
		if( !preferOfflineRecognition )
			return 1;
#if __IPHONE_OS_VERSION_MAX_ALLOWED >= 130000
		else if( CHECK_IOS_VERSION( @"13.0" ) && [speechRecognizer supportsOnDeviceRecognition] )
			return 1;
#endif
	}
	
	return 0;
}

+ (int)isBusy
{
	return ( CHECK_IOS_VERSION( @"10.0" ) && recognitionRequest != nil ) ? 1 : 0;
}

+ (float)getAudioRmsdB
{
	return audioRmsdB;
}

// Credit: https://stackoverflow.com/a/20464727/2373034
+ (int)checkPermission
{
	if( CHECK_IOS_VERSION( @"10.0" ) )
	{
		SFSpeechRecognizerAuthorizationStatus status = [SFSpeechRecognizer authorizationStatus];
		if( status == SFSpeechRecognizerAuthorizationStatusAuthorized )
			return 1;
		else if( status == SFSpeechRecognizerAuthorizationStatusNotDetermined )
			return 2;
	}
	
	return 0;
}

+ (int)requestPermission:(BOOL)asyncMode
{
	int result = [self requestPermissionInternal:asyncMode];
	if( asyncMode && result >= 0 ) // Result returned immediately, forward it
		UnitySendMessage( "STTPermissionCallbackiOS", "OnPermissionRequested", [self getCString:[NSString stringWithFormat:@"%d", result]] );
		
	return result;
}

+ (int)requestPermissionInternal:(BOOL)asyncMode
{
	if( !CHECK_IOS_VERSION( @"10.0" ) )
		return 0;
	
	SFSpeechRecognizerAuthorizationStatus status = [SFSpeechRecognizer authorizationStatus];
	if( status == SFSpeechRecognizerAuthorizationStatusAuthorized )
		return 1;
	else if( status == SFSpeechRecognizerAuthorizationStatusNotDetermined )
	{
		if( asyncMode )
		{
			[SFSpeechRecognizer requestAuthorization:^( SFSpeechRecognizerAuthorizationStatus status )
			{
				UnitySendMessage( "STTPermissionCallbackiOS", "OnPermissionRequested", ( status == SFSpeechRecognizerAuthorizationStatusAuthorized ) ? "1" : "0" );
			}];
			
			return -1;
		}
		else
		{
			__block BOOL authorized = NO;
			dispatch_semaphore_t sema = dispatch_semaphore_create( 0 );
			[SFSpeechRecognizer requestAuthorization:^( SFSpeechRecognizerAuthorizationStatus status )
			{
				authorized = ( status == SFSpeechRecognizerAuthorizationStatusAuthorized );
				dispatch_semaphore_signal( sema );
			}];
			dispatch_semaphore_wait( sema, DISPATCH_TIME_FOREVER );
			
			return authorized ? 1 : 0;
		}
	}
	
	return 0;
}

#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
+ (void)openSettings
{
	if( &UIApplicationOpenSettingsURLString != NULL )
	{
#if __IPHONE_OS_VERSION_MAX_ALLOWED >= 100000
		if( CHECK_IOS_VERSION( @"10.0" ) )
			[[UIApplication sharedApplication] openURL:[NSURL URLWithString:UIApplicationOpenSettingsURLString] options:@{} completionHandler:nil];
		else
#endif
			[[UIApplication sharedApplication] openURL:[NSURL URLWithString:UIApplicationOpenSettingsURLString]];
	}
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

extern "C" int _SpeechToText_RequestPermission( int asyncMode )
{
	return [USpeechToText requestPermission:( asyncMode == 1 )];
}

extern "C" void _SpeechToText_OpenSettings()
{
	[USpeechToText openSettings];
}