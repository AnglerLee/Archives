using System;
using System.Collections.Generic;
using System.Timers;
using Tizen.NUI;
using Tizen.NUI.Scene3D;

namespace Tizen.AIAvatar.NUI
{
    public class LipSyncer
    {
        private Model avatar;
        private Animation currentAnimation;
        private Animation silenceAnimation;
        private Queue<Animation> queuedAnimations = new Queue<Animation>();
        private Timer animationTimer;

        private readonly AlphaFunction alphaFunction = new AlphaFunction(AlphaFunction.BuiltinFunctions.EaseInOut);
        private readonly LipSyncTransformer lipSyncTransformer = new LipSyncTransformer();
        private AnimatorState currentAnimatorState = AnimatorState.Unavailable;

        public event EventHandler<AnimatorChangedEventArgs> AnimatorStateChanged;

        public LipSyncer()
        {
            silenceAnimation = GenerateAnimationFromVowels(new[] { "sil", "sil" }, 0.5f);
        }

        public void Initialize(Model avatar, string visemeDefinitionPath)
        {
            this.avatar = avatar;
            lipSyncTransformer.Initialize(visemeDefinitionPath);
        }

        public void Enqueue(Animation lipAnimation)
        {
            queuedAnimations.Enqueue(lipAnimation);

            // 애니메이션이 재생 중이 아니면 바로 시작
            if (animationTimer == null || !animationTimer.Enabled)
            {
                PlayNextAnimation();
            }
        }

        private void PlayNextAnimation()
        {
            // 현재 애니메이션을 정지 및 해제
            currentAnimation?.Stop();
            currentAnimation?.Dispose();
            currentAnimation = null;

            if (queuedAnimations.Count > 0)
            {
                currentAnimation = queuedAnimations.Dequeue();
                currentAnimation.Play();
                SetAnimatorState(AnimatorState.Playing);

                // 현재 애니메이션의 Duration을 기준으로 타이머 설정
                animationTimer = new Timer(currentAnimation.Duration * 1000); // 밀리초 단위
                animationTimer.Elapsed += (s, e) => PlayNextAnimation();
                animationTimer.AutoReset = false; // 한 번만 실행 후 멈춤
                animationTimer.Start();
            }
            else
            {
                PlaySilenceAnimation();
            }
        }

        private void PlaySilenceAnimation()
        {
            currentAnimation = silenceAnimation;
            currentAnimation.Play();
            SetAnimatorState(AnimatorState.AnimationFinished);

            // Silence 애니메이션은 반복 재생
            animationTimer = new Timer(silenceAnimation.Duration * 1000);
            animationTimer.Elapsed += (s, e) => PlaySilenceAnimation();
            animationTimer.AutoReset = true;
            animationTimer.Start();
        }

        private void SetAnimatorState(AnimatorState newState)
        {
            if (currentAnimatorState == newState) return;

            var previousState = currentAnimatorState;
            currentAnimatorState = newState;
            AnimatorStateChanged?.Invoke(this, new AnimatorChangedEventArgs(previousState, currentAnimatorState));
        }

        public Animation GenerateAnimationFromVowels(string[] vowels, float stepTime = 0.08f, bool isStreaming = false)
        {
            var lipData = lipSyncTransformer.TransformVowelsToLipData(vowels, stepTime, isStreaming);
            using var motionData = GenerateMotionFromLipData(lipData);
            return avatar.GenerateMotionDataAnimation(motionData);
        }

        private MotionData GenerateMotionFromLipData(LipData animationKeyFrames)
        {
            int animationTime = (int)(animationKeyFrames.Duration * 1000f);
            var motionData = new MotionData(animationTime);

            for (int i = 0; i < animationKeyFrames.NodeNames.Length; i++)
            {
                string nodeName = animationKeyFrames.NodeNames[i];
                for (int j = 0; j < animationKeyFrames.BlendShapeCounts[i]; j++)
                {
                    using var modelNodeID = new PropertyKey(nodeName);
                    using var blendShapeID = new PropertyKey(j);
                    var blendShapeIndex = new BlendShapeIndex(modelNodeID, blendShapeID);
                    var keyFrameList = animationKeyFrames.GetKeyFrames(nodeName, j);

                    if (keyFrameList.Count == 0) continue;

                    motionData.Add(blendShapeIndex, new MotionValue(CreateKeyTimeFrames(keyFrameList)));
                }
            }

            return motionData;
        }

        private KeyFrames CreateKeyTimeFrames(List<KeyFrame> keyFrameList)
        {
            var keyFrames = new KeyFrames();
            foreach (var key in keyFrameList)
            {
                keyFrames.Add(key.time, key.value, alphaFunction);
            }

            return keyFrames;
        }
    }
}
