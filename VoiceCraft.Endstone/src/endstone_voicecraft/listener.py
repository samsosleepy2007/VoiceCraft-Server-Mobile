from endstone.event import PlayerJoinEvent, PlayerQuitEvent, event_handler


class VoiceCraftListener:
    def __init__(self, plugin) -> None:
        self._plugin = plugin

    @event_handler
    def on_player_join(self, event: PlayerJoinEvent) -> None:
        self._plugin.handle_player_join(event.player)

    @event_handler
    def on_player_quit(self, event: PlayerQuitEvent) -> None:
        self._plugin.handle_player_quit(event.player)
