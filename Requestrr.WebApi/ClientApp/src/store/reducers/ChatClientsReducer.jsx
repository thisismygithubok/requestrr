import { GET_SETTINGS } from "../actions/ChatClientsActions"

export default function ChatClientsReducer(state = {}, action) {
  if (action.type === GET_SETTINGS) {
    return {
      chatClient: action.payload.client,
      clientId: action.payload.clientId,
      botToken: action.payload.botToken,
      statusMessage: action.payload.statusMessage,
      monitoredChannels: action.payload.monitoredChannels,
      tvShowRoles: action.payload.tvShowRoles,
      movieRoles: action.payload.movieRoles,
      musicRoles: action.payload.musicRoles,
      enableRequestsThroughDirectMessages: action.payload.enableRequestsThroughDirectMessages,
      automaticallyNotifyRequesters: action.payload.automaticallyNotifyRequesters,
      notificationMode: action.payload.notificationMode,
      notificationChannels: action.payload.notificationChannels,
      statusNotifications: action.payload.statusNotifications,
      automaticallyPurgeCommandMessages: action.payload.automaticallyPurgeCommandMessages,
      language: action.payload.language,
      availableLanguages: action.payload.availableLanguages,
    };
  }

  return { ...state };
}