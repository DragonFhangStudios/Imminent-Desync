using UnityEngine;

// Experiment 1: how contaminated Desync intensity is displayed.
// Current = continuous orange->yellow ramp (opacity varies with intensity).
// Bands   = stepped color bands at doubling thresholds (fixed overlay strength).
public enum DesyncDisplayMode
{
	Current,
	Bands
}

public class CellVisual : MonoBehaviour
{
	[SerializeField] private SpriteRenderer spriteRenderer;

	[Header("Terrain (Phase 1 - visual only)")]
	[SerializeField]
	private Color terrainLowColor = new(0.10f, 0.11f, 0.13f);

	[SerializeField]
	private Color terrainHighColor = new(0.45f, 0.44f, 0.41f);

	[Header("Desync Overlay")]
	[SerializeField]
	private Color desyncLowColor = new(0.90f, 0.35f, 0.08f);

	[SerializeField]
	private Color desyncHighColor = new(1.00f, 0.88f, 0.35f);

	[Header("Desync Display (Experiment 1)")]
	[SerializeField]
	[Tooltip("Current = continuous orange->yellow ramp. Bands = stepped color bands at Desync 5 / 10 / 20 / 40 / 80 / 160+, plus a white cap band at Max Desync. Set on the CellVisual prefab before entering Play mode.")]
	private DesyncDisplayMode desyncDisplayMode = DesyncDisplayMode.Current;

	[SerializeField, Range(0.2f, 0.7f)]
	[Tooltip("Bands mode only: how strongly the band color is blended over terrain. The same for every band, so color (not opacity) carries intensity.")]
	private float bandOverlayStrength = 0.45f;

	[SerializeField]
	[Tooltip("Bands mode only: one color per band, lowest to highest: [5-10), [10-20), [20-40), [40-80), [80-160), 160+.")]
	private Color[] bandColors =
	{
		new(0.60f, 0.50f, 0.34f), // 5-10    dull ochre (mostly covered by the cyan edge by default)
		new(0.85f, 0.62f, 0.22f), // 10-20   amber
		new(0.98f, 0.45f, 0.10f), // 20-40   orange
		new(0.92f, 0.16f, 0.10f), // 40-80   red
		new(0.82f, 0.08f, 0.52f), // 80-160  magenta
		new(1.00f, 0.62f, 0.95f)  // 160+    hot pink-white
	};

	[SerializeField]
	[Tooltip("Bands mode only: color for cells at Max Desync (saturated / at the cap).")]
	private Color capBandColor = Color.white;

	[SerializeField, Range(0.2f, 1f)]
	[Tooltip("Bands mode only: how strongly the cap color is blended over terrain. Stronger than the other bands so saturation reads clearly as white.")]
	private float capOverlayStrength = 0.8f;

	// Lower bound of each band (Desync units). Band i covers [BandThresholds[i], BandThresholds[i+1]).
	private static readonly float[] BandThresholds = { 5f, 10f, 20f, 40f, 80f, 160f };

	[Header("Contamination Edge")]
	[SerializeField]
	private Color contaminationEdgeColor = new(0.15f, 0.85f, 1.00f);

	// Cells with desync in [threshold, threshold + band) are tinted cyan.
	// Because desync falls off smoothly, these cells form a ring at the
	// contamination front. Set to 0 to disable the ring.
	[SerializeField, Min(0f)]
	private float contaminationEdgeBand = 5f;

	// Mirrors GridManager.contaminationThreshold and maxDesync.
	// If those change in GridManager, update these to match.
	private const float ContaminationThreshold = 5f;
	private const float MaxDesync = 250f;

	// Fixed overlay opacities (terrain always shows through).
	private const float ContaminatedOpacityMin = 0.10f;
	private const float ContaminatedOpacityMax = 0.35f;
	private const float EdgeBlend = 0.70f;

	// Cached once when elevation is assigned.
	private Color terrainColor = Color.black;

	public void SetElevation(float elevation)
	{
		terrainColor =
			Color.Lerp(
				terrainLowColor,
				terrainHighColor,
				Mathf.Clamp01(elevation));

		spriteRenderer.color = terrainColor;
	}

	public void SetCell(float desync, float stabilization)	
	{
		// Layer order: terrain -> contaminated desync -> contamination edge -> stabilization

		Color finalColor = terrainColor;

		// ----------------------------
		// DESYNC OVERLAY
		// ----------------------------

		// Desync below the contamination threshold is not drawn;
		// those cells show plain terrain.
		if (desync >= ContaminationThreshold)
		{
			if (desyncDisplayMode == DesyncDisplayMode.Bands)
			{
				if (desync >= MaxDesync)
				{
					// Cap band: the cell is saturated at Max Desync.
					finalColor =
						Color.Lerp(
							terrainColor,
							capBandColor,
							capOverlayStrength);
				}
				else
				{
					// Stepped band color at a fixed overlay strength: color carries intensity.
					finalColor =
						Color.Lerp(
							terrainColor,
							GetBandColor(desync),
							bandOverlayStrength);
				}
			}
			else
			{
				// Contaminated desync: orange -> warm yellow, stronger with intensity.
				// Square-root curve spreads the common 5..100 range visually.

				float intensity =
					Mathf.Sqrt(
						Mathf.Clamp01(
							(desync - ContaminationThreshold) /
							(MaxDesync - ContaminationThreshold)));

				Color desyncTint =
					Color.Lerp(
						desyncLowColor,
						desyncHighColor,
						intensity);

				float contaminatedOpacity =
					Mathf.Lerp(
						ContaminatedOpacityMin,
						ContaminatedOpacityMax,
						intensity);

				finalColor =
					Color.Lerp(
						terrainColor,
						desyncTint,
						contaminatedOpacity);
			}

			// ----------------------------
			// CONTAMINATION EDGE (cyan front band)
			// ----------------------------

			if (desync < ContaminationThreshold + contaminationEdgeBand)
			{
				finalColor =
					Color.Lerp(
						finalColor,
						contaminationEdgeColor,
						EdgeBlend);
			}
		}

		// ----------------------------
		// STABILIZATION OVERLAY
		// ----------------------------

		if (stabilization > 0f)
		{
			float stabilizationStrength =
			Mathf.Clamp01(
			stabilization / 10f);

			Color stabilizationColor =
				Color.Lerp(
					Color.black,
					Color.green,
					stabilizationStrength);

			finalColor =
				Color.Lerp(
					finalColor,
					stabilizationColor,
					stabilizationStrength);

		}

		spriteRenderer.color = finalColor;
	}

	private Color GetBandColor(float desync)
	{
		if (bandColors == null || bandColors.Length == 0)
			return desyncHighColor;

		int band = 0;

		for (int i = BandThresholds.Length - 1; i >= 0; i--)
		{
			if (desync >= BandThresholds[i])
			{
				band = i;
				break;
			}
		}

		// If the Inspector array is shorter than 6, reuse its last color.
		return bandColors[Mathf.Min(band, bandColors.Length - 1)];
	}
}
