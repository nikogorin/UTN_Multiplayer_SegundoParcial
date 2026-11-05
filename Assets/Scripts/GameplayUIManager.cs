using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class GameplayUIManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject hudPanel;
    [SerializeField] private GameObject waitingPanel;
    [SerializeField] private GameObject eliminatedPanel;
    [SerializeField] private GameObject resultsPanel;

    [Header("Health HUD")]
    [SerializeField] private Image healthFill;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private Image fireballFill;

    [Header("Players HUD")]
    [SerializeField] private TMP_Text remainingPlayerText;
    

    [Header("Danger Zone HUD")]
    [SerializeField] private DangerZone dangerZone;
    [SerializeField] private Image dangerIcon;
    [SerializeField] private TMP_Text dangerText;
    [SerializeField] private Image arrowIcon;

    [Header("Eliminate")]
    [SerializeField] private GameObject eliminatedLeaveButton;

    [Header("Results")]
    [SerializeField] private TMP_Text resultTitle;
    [SerializeField] private TMP_Text resultDescription;

    private PlayerHealth _localPlayerHealth;
    private PlayerShooting _localPlayerShooting;

    private void Start()
    {
        GameManager.Instance.OnGameStateChanged += UpdateUI;
        GameManager.Instance.OnAlivePlayersCountChanged += UpdateRemainingPlayerUI;
        GameManager.Instance.OnWinnerChanged += UpdateWinnerUI;
        dangerZone.OnStateUpdated += UpdateDangerZoneUI;

        UpdateUI(GameManager.Instance.CurrentState);
        UpdateRemainingPlayerUI(GameManager.Instance.AlivePlayersCount);
        UpdateDangerZoneUI(dangerZone.CurrentState);
    }

    private void Update()
    {
        if (_localPlayerHealth == null || _localPlayerShooting == null)
            BindLocalPlayer();

        if (_localPlayerShooting != null)
            UpdateFireballUI(_localPlayerShooting.CooldownProgress);
    }

    private void OnDestroy()
    {
        if(GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStateChanged -= UpdateUI;
            GameManager.Instance.OnAlivePlayersCountChanged -= UpdateRemainingPlayerUI;
            GameManager.Instance.OnWinnerChanged -= UpdateWinnerUI;
        }

        if (dangerZone != null)
            dangerZone.OnStateUpdated -= UpdateDangerZoneUI;

        if (_localPlayerHealth != null)
            _localPlayerHealth.OnHealthUpdated -= UpdateHealthUI;
    }

    private void UpdateUI(GameState state)
    {

        waitingPanel.SetActive(state == GameState.Waiting);
        hudPanel.SetActive(state == GameState.Playing);
        resultsPanel.SetActive(state == GameState.Finished);

        var eliminatePanelActive = state == GameState.Playing && _localPlayerHealth != null && _localPlayerHealth.IsDead;
        eliminatedPanel.SetActive(eliminatePanelActive);

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        eliminatedLeaveButton.SetActive(!isHost);

        if (state == GameState.Finished)
            UpdateResultsUI();
    }

    private void UpdateRemainingPlayerUI(int alivePlayers)
    {
        remainingPlayerText.text = $"{alivePlayers} Jugadores";
    }

    private void UpdateHealthUI(int currentHealth, int maxHealth)
    {
        healthFill.fillAmount = maxHealth > 0 ? (float)currentHealth / maxHealth : 0f;
        healthText.text = $"{currentHealth} / {maxHealth}";

        eliminatedPanel.SetActive(currentHealth <= 0 && GameManager.Instance.IsPlaying);
    }

    private void UpdateFireballUI(float cooldownProgress)
    {
        fireballFill.fillAmount = Mathf.Clamp01(cooldownProgress);
    }

    private void UpdateWinnerUI(ulong winnerId)
    {
        if (GameManager.Instance.CurrentState == GameState.Finished)
            UpdateResultsUI();
    }

    private void UpdateResultsUI()
    {
        ulong winnerId = GameManager.Instance.WinnerClientId;
        ulong localId = NetworkManager.Singleton.LocalClientId;

        if (winnerId == ulong.MaxValue)
        {
            resultTitle.text = "¡EMPATE!";
            resultTitle.color = Color.yellow;
            resultDescription.text = "No quedaron supervivientes.";
        }
        else if (winnerId == localId)
        {
            resultTitle.text = "¡VICTORIA!";
            resultTitle.color = Color.green;
            resultDescription.text = "Sos el último superviviente.";
        }
        else
        {
            resultTitle.text = "DERROTA";
            resultTitle.color = Color.red;
            resultDescription.text = "Otro jugador ganó la partida.";
        }
    }

    private void UpdateDangerZoneUI(DangerZoneState state)
    {
        dangerIcon.enabled = state == DangerZoneState.Moving;
        arrowIcon.enabled = state == DangerZoneState.Moving;
        dangerText.text = state == DangerZoneState.Moving ? "ZONA SUBIENDO" : "ZONA DETENIDA";
        dangerText.color = state == DangerZoneState.Moving ? Color.red : Color.green;
    }

    private void BindLocalPlayer()
    {
        PlayerSession session = PlayerSession.Local;

        if (session == null) 
            return;

        if(!session.CurrentAvatar.Value.TryGet(out NetworkObject avatar))
            return;

        if (!avatar.TryGetComponent(out PlayerHealth playerHealth))
            return;

        if (!avatar.TryGetComponent(out PlayerShooting playerShooting))
            return;

        _localPlayerHealth = playerHealth;
        _localPlayerShooting = playerShooting;
        _localPlayerHealth.OnHealthUpdated += UpdateHealthUI;

        UpdateHealthUI(_localPlayerHealth.CurrentHealth, _localPlayerHealth.MaxHealth);
        UpdateFireballUI(_localPlayerShooting.CooldownProgress);
        UpdateUI(GameManager.Instance.CurrentState);
    }

    // ----------- Could be move to an separated UI Component
    public async void OnLeaveGamePressed()
    {
        Debug.Log("OnLeaveGamePressed");

        await MultiplayerConnectionManager.Instance.LeaveGame();
    }

    public async void OnEliminatedLeavePressed()
    {
        Debug.Log("OnEliminatedLeavePressed");

        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null || manager.IsHost)
            return;

        await MultiplayerConnectionManager.Instance.LeaveGame();
    }
}
