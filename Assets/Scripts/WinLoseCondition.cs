using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Linq;
using System.Collections.Generic;

public class WinLoseCondition : MonoBehaviour
{
    [Header("Config - Key player units")]
    [SerializeField] private Unit[] playerCharacterUnits;
    [SerializeField] private HQUnit playerHQ;

    [Header("Config - Victory/Defeat panel")]
    [SerializeField] private GameObject panel;
    [SerializeField] private float fadeDuration = 1f;
    [SerializeField] private Image victoryDefeatBannerImg;
    [SerializeField] private Sprite victoryBannerSprite;
    [SerializeField] private Sprite defeatBannerSprite;

    void Start()
    {
        GameManager.Instance.OnGameStateChanged += HandleGameStateChanged;

        foreach (var unit in playerCharacterUnits)
        {
            unit.OnUnitDeath += HandleUnitDeath;
        }
        playerHQ.OnUnitDeath += HandleHQDeath;

        panel.SetActive(false);

        Debug.Log("=== WinLoseCondition initialized and listeners set up. ===");
    }

    private void HandleGameStateChanged(GameState state)
    {
        if(state == GameState.Victory || state == GameState.Defeat)
        {
            HyenasManager.Instance.HaltAllRemainingHyenaMovesAndDoNotNotifyGameManager();

            CanvasGroup canvasGroup = panel.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0;
            victoryDefeatBannerImg.sprite = state == GameState.Victory ? victoryBannerSprite : defeatBannerSprite;
            panel.SetActive(true);
            canvasGroup.DOFade(1f, fadeDuration);

            SoundsManager.Instance.PlayMusic(state == GameState.Victory ? BGM.Victory : BGM.Defeat);
        }
    }

    private void HandleUnitDeath(Unit unit)
    {
        Debug.Log($"Unit {unit.name} has died. Checking for loss condition...");

        List<Unit> aliveUnits = playerCharacterUnits.Where(unit => unit.IsAlive()).ToList();
        if (aliveUnits.Count == 0)
        {
            Debug.Log("All key player units have died. Player loses.");
            GameManager.Instance.OnPlayerDefeat();
        }
        else
        {
            LogUtils.LogEnumerable("These units are still alive", aliveUnits);
        }
    }

    private void HandleHQDeath(Unit hq)
    {
        Debug.Log($"Unit {hq.name} has died. Player loses.");
        GameManager.Instance.OnPlayerDefeat();
    }
}
