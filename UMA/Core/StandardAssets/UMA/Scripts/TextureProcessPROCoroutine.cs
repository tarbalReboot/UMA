using UnityEngine;
using System.Collections;
using System;

namespace UMA
{
	/// <summary>
	/// Texture processing coroutine using rendertextures for atlas building.
	/// </summary>
	[Serializable]
	public class TextureProcessPROCoroutine : TextureProcessBaseCoroutine
	{
		UMAData umaData;
		RenderTexture destinationTexture;
		Texture[] resultingTextures;
		UMAGeneratorBase umaGenerator;
		Camera renderCamera;
		bool fastPath=false;

		Color emptyCameraBG = new Color(0, 0, 0, 0);
		Color normalCameraBG = Color.grey;

		/// <summary>
		/// Setup data for atlas building.
		/// </summary>
		/// <param name="_umaData">UMA data.</param>
		/// <param name="_umaGenerator">UMA generator.</param>
		public override void Prepare(UMAData _umaData, UMAGeneratorBase _umaGenerator)
		{
			umaData = _umaData;
			umaGenerator = _umaGenerator;
			if (umaGenerator is UMAGenerator)
			{
				fastPath = (umaGenerator as UMAGenerator).fastGeneration;
			}
			if (umaData.atlasResolutionScale <= 0) umaData.atlasResolutionScale = 1f;
		}

		protected override void Start()
		{

		}

		public static RenderTexture ResizeRenderTexture(RenderTexture source, int newWidth, int newHeight, FilterMode filter)
		{
			source.filterMode = filter;
			RenderTexture rt = new RenderTexture(newWidth, newHeight, 0, source.format, RenderTextureReadWrite.Linear);

			rt.filterMode = FilterMode.Point;

			RenderTexture.active = rt;
			Graphics.Blit(source, rt);
			return rt;
		}

	  protected override IEnumerator workerMethod()
	  {
		 var textureMerge = umaGenerator.textureMerge;

		 for (int atlasIndex = umaData.generatedMaterials.materials.Count - 1; atlasIndex >= 0; atlasIndex--)
		 {
			var atlas = umaData.generatedMaterials.materials[atlasIndex];

			//Rendering Atlas
			int moduleCount = 0;

			//Process all necessary TextureModules
			for (int i = 0; i < atlas.materialFragments.Count; i++)
			{
			   if (!atlas.materialFragments[i].isRectShared)
			   {
				  moduleCount++;
				  moduleCount = moduleCount + atlas.materialFragments[i].overlays.Length;
			   }
			}
			textureMerge.EnsureCapacity(moduleCount);

			var slotData = atlas.materialFragments[0].slotData;
			resultingTextures = new Texture[slotData.asset.material.channels.Length];
			for (int textureType = slotData.asset.material.channels.Length - 1; textureType >= 0; textureType--)
			{
			   switch (slotData.asset.material.channels[textureType].channelType)
			   {
				  case UMAMaterial.ChannelType.Texture:
				  case UMAMaterial.ChannelType.DiffuseTexture:
				  case UMAMaterial.ChannelType.NormalMap:
				  {
					 textureMerge.Reset();
					 for (int i = 0; i < atlas.materialFragments.Count; i++)
					 {
						textureMerge.SetupModule(atlas, i, textureType);
					 }

					 //last element for this textureType
					 moduleCount = 0;

					 umaGenerator.textureMerge.gameObject.SetActive(true);

					 int width = Mathf.FloorToInt(atlas.cropResolution.x);
					 int height = Mathf.FloorToInt(atlas.cropResolution.y);
					 destinationTexture = new RenderTexture(Mathf.FloorToInt(atlas.cropResolution.x * umaData.atlasResolutionScale), Mathf.FloorToInt(atlas.cropResolution.y * umaData.atlasResolutionScale), 0, slotData.asset.material.channels[textureType].textureFormat, RenderTextureReadWrite.Linear);
					 destinationTexture.filterMode = FilterMode.Point;
					 destinationTexture.useMipMap = umaGenerator.convertMipMaps && !umaGenerator.convertRenderTexture;
					 renderCamera = umaGenerator.textureMerge.myCamera;

                    if (slotData.asset.material.channels[textureType].channelType == UMAMaterial.ChannelType.NormalMap)
                    {
                        if (QualitySettings.desiredColorSpace == ColorSpace.Linear)
                        {
                            //Due to a weird conversion, we need the camera background to be grey in gamme color space.
                            renderCamera.backgroundColor = normalCameraBG.gamma;
                        }
                        else
                        {
                            renderCamera.backgroundColor = normalCameraBG;
                        }                                    
                    }
                    else
                    {
                        renderCamera.backgroundColor = emptyCameraBG;
                    }
                               

					 renderCamera.targetTexture = destinationTexture;
					 renderCamera.orthographicSize = height >> 1;
					 var camTransform = renderCamera.GetComponent<Transform>();
					 camTransform.position = new Vector3(width >> 1, height >> 1, 3);
					 camTransform.rotation = Quaternion.Euler(0, 180, 180);
					 renderCamera.Render();

					 int DownSample = slotData.asset.material.channels[textureType].DownSample;

					 if (DownSample != 0)
					 {
						int newW = width >> DownSample;
						int newH = height >> DownSample;

						RenderTexture rt = ResizeRenderTexture(destinationTexture, newW, newH, FilterMode.Bilinear);
						destinationTexture.Release();
						destinationTexture = rt;
					 }

					 renderCamera.gameObject.SetActive(false);
					 renderCamera.targetTexture = null;

					 if (umaGenerator.convertRenderTexture || slotData.asset.material.channels[textureType].ConvertRenderTexture)
					 {
						#region Convert Render Textures
						if (!fastPath) yield return 25;
						Texture2D tempTexture;

						tempTexture = new Texture2D(destinationTexture.width, destinationTexture.height, TextureFormat.ARGB32, umaGenerator.convertMipMaps, true);

						int xblocks = destinationTexture.width / 512;
						int yblocks = destinationTexture.height / 512;
						if (xblocks == 0 || yblocks == 0 || fastPath)
						{
						   RenderTexture.active = destinationTexture;
						   tempTexture.ReadPixels(new Rect(0, 0, destinationTexture.width, destinationTexture.height), 0, 0, umaGenerator.convertMipMaps);
						   RenderTexture.active = null;
						}
						else
						{
						   // figures that ReadPixels works differently on OpenGL and DirectX, someday this code will break because Unity fixes this bug!
						   if (IsOpenGL())
						   {
							  for (int x = 0; x < xblocks; x++)
							  {
								 for (int y = 0; y < yblocks; y++)
								 {
									RenderTexture.active = destinationTexture;
									tempTexture.ReadPixels(new Rect(x * 512, y * 512, 512, 512), x * 512, y * 512, umaGenerator.convertMipMaps);
									RenderTexture.active = null;
									yield return 8;
								 }
							  }
						   }
						   else
						   {
							  for (int x = 0; x < xblocks; x++)
							  {
								 for (int y = 0; y < yblocks; y++)
								 {
									RenderTexture.active = destinationTexture;
									tempTexture.ReadPixels(new Rect(x * 512, destinationTexture.height - 512 - y * 512, 512, 512), x * 512, y * 512, umaGenerator.convertMipMaps);
									RenderTexture.active = null;
									yield return 8;
								 }
							  }
						   }
						}


						resultingTextures[textureType] = tempTexture as Texture;

						renderCamera.targetTexture = null;
						RenderTexture.active = null;

						destinationTexture.Release();
						UnityEngine.GameObject.DestroyImmediate(destinationTexture);
						umaGenerator.textureMerge.gameObject.SetActive(false);
						if (!fastPath) yield return 6;
						tempTexture = resultingTextures[textureType] as Texture2D;
						tempTexture.Apply();
						tempTexture.wrapMode = TextureWrapMode.Repeat;
						tempTexture.anisoLevel = slotData.asset.material.AnisoLevel;
						tempTexture.mipMapBias = slotData.asset.material.MipMapBias;
						tempTexture.filterMode = slotData.asset.material.MatFilterMode;
						if (slotData.asset.material.channels[textureType].Compression != UMAMaterial.CompressionSettings.None)
						{
						   tempTexture.Compress(slotData.asset.material.channels[textureType].Compression == UMAMaterial.CompressionSettings.HighQuality);
						}
						resultingTextures[textureType] = tempTexture;
						atlas.material.SetTexture(slotData.asset.material.channels[textureType].materialPropertyName, tempTexture);
						#endregion
					 }
					 else
					 {
						destinationTexture.anisoLevel = slotData.asset.material.AnisoLevel;
						destinationTexture.mipMapBias = slotData.asset.material.MipMapBias;
						destinationTexture.filterMode = slotData.asset.material.MatFilterMode;
						destinationTexture.wrapMode = TextureWrapMode.Repeat;
						resultingTextures[textureType] = destinationTexture;
						atlas.material.SetTexture(slotData.asset.material.channels[textureType].materialPropertyName, destinationTexture);
					 }

					 umaGenerator.textureMerge.gameObject.SetActive(false);
					 break;
				  }
				  case UMAMaterial.ChannelType.MaterialColor:
				  {
					 atlas.material.SetColor(slotData.asset.material.channels[textureType].materialPropertyName, atlas.materialFragments[0].baseColor);
					 break;
				  }
				  case UMAMaterial.ChannelType.TintedTexture:
				  {
					 for (int i = 0; i < atlas.materialFragments.Count; i++)
					 {
						var fragment = atlas.materialFragments[i];
						if (fragment.isRectShared) continue;
						for (int j = 0; j < fragment.baseOverlay.textureList.Length; j++)
						{
						   if (fragment.baseOverlay.textureList[j] != null)
						   {
							  atlas.material.SetTexture(slotData.asset.material.channels[j].materialPropertyName, fragment.baseOverlay.textureList[j]);
							  if (j == 0)
							  {
								 atlas.material.color = fragment.baseColor;
							  }
						   }
						}
						foreach (var overlay in fragment.overlays)
						{
						   for (int j = 0; j < overlay.textureList.Length; j++)
						   {
							  if (overlay.textureList[j] != null)
							  {
								 atlas.material.SetTexture(slotData.asset.material.channels[j].materialPropertyName, overlay.textureList[j]);
							  }
						   }
						}
					 }
					 break;
				  }
			   }
			}
			atlas.resultingAtlasList = resultingTextures;
		 }
	  }

	  private bool IsOpenGL()
		{
			var graphicsDeviceVersion = SystemInfo.graphicsDeviceVersion;
			return graphicsDeviceVersion.StartsWith("OpenGL");
		}

		protected override void Stop()
		{
			
		}
	}
}
