using System;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Central prop-generation controller on RoomLevelGen. Applies shared settings to every placed room's Content root.
    /// </summary>
    public class RoomContentManager : MonoBehaviour
    {
        [SerializeField] private RoomConnector _connector;
        [SerializeField] private RoomContentSettings _settings;
        [SerializeField] private bool _activateAfterBuild = true;

        private void Awake()
        {
            if (_connector == null)
                _connector = GetComponent<RoomConnector>();
        }

        public void ActivateAll(int levelSeed)
        {
            if (!_activateAfterBuild || _settings == null)
                return;

            if (_connector == null || _connector.LevelRoot == null)
            {
                Debug.LogWarning("[RoomContent] No LevelRoot found — skipping content activation.", this);
                return;
            }

            foreach (RoomInstance room in _connector.LevelRoot.GetComponentsInChildren<RoomInstance>())
            {
                Transform content = room.transform.Find("Content");
                if (content == null)
                    continue;

                Vector3 position = room.transform.position;
                int positionKey = HashCode.Combine(
                    Mathf.RoundToInt(position.x * 100f),
                    Mathf.RoundToInt(position.y * 100f),
                    Mathf.RoundToInt(position.z * 100f));
                int roomSeed = HashCode.Combine(levelSeed, positionKey);

                RoomContentActivation.Apply(content, _settings, roomSeed, room);
            }

            RefreshOpenWalls();
        }

        private void RefreshOpenWalls()
        {
            if (_connector == null || _connector.LevelRoot == null)
                return;

            foreach (RoomInstance room in _connector.LevelRoot.GetComponentsInChildren<RoomInstance>())
            {
                foreach (WallGapController wall in room.Walls)
                {
                    if (wall != null && wall.hasOpening)
                        wall.ApplyGap();
                }
            }
        }
    }
}
