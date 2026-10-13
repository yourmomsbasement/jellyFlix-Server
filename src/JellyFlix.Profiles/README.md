# JellyFlix Profiles

Netflix-style household profiles for Jellyfin: sign in once, then pick "Who's watching?".

Each profile is backed by its own hidden Jellyfin user, so watch history, Continue Watching,
Next Up and parental controls are tracked per profile by the stock server — and every
Jellyfin client keeps working. The plugin only adds the household on top:

- The **owner** is a normal Jellyfin account. Signing in with it unlocks the household.
- **Profiles** are created by the owner. Their users are hidden from the login screen and get
  a random password, so the only way in is switching from inside the household.
- Profiles inherit the owner's library access, minus anything administrative.
  **Kids** profiles additionally get a parental rating limit and hide unrated items.
- Any profile can have a 4-digit **PIN**. Five wrong PINs lock that profile for 15 minutes.
  Set a PIN on the owner profile if kids profiles shouldn't be able to switch into it.
- Switching issues a new session for the same device and signs out the previous one.

## Build and install

The plugin is built against this repository's server projects (it targets the same server version):

```sh
dotnet build src/JellyFlix.Profiles -c Release
mkdir -p "<jellyfin data dir>/plugins/JellyFlix Profiles_1.0.0.0"
cp src/JellyFlix.Profiles/bin/Release/net10.0/JellyFlix.Profiles.dll "<jellyfin data dir>/plugins/JellyFlix Profiles_1.0.0.0/"
```

Restart the server. Settings are under Dashboard → Plugins → JellyFlix Profiles.

## API

All endpoints need a user session (not an API key).

| Method | Path | Who | Description |
| --- | --- | --- | --- |
| `GET` | `/JellyFlix/Profiles` | any profile | The household and its profiles. |
| `POST` | `/JellyFlix/Profiles` | owner | Add a profile: `{ Name, AvatarColor?, IsKids, Pin? }`. |
| `POST` | `/JellyFlix/Profiles/{id}` | owner, or the profile itself | Change `{ Name?, AvatarColor?, IsKids?, Pin?, RemovePin }`. Only the owner may change `IsKids`. |
| `DELETE` | `/JellyFlix/Profiles/{id}` | owner | Remove a profile **and its watch history**. |
| `POST` | `/JellyFlix/Profiles/{id}/Switch` | any profile | `{ Pin? }` → an `AuthenticationResult` for the target profile on the calling device. |

Errors return `{ Error, Message }` with 400, 403 (`Forbidden`, `InvalidPin`), 404 or 429 (`PinLocked`).
401 is never used for refusals, because clients treat it as "signed out".

Households are stored in `<data dir>/data/jellyflix-profiles/households.json`.
