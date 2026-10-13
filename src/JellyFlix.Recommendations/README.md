# JellyFlix Recommendations

Personalized rows for the JellyFlix web client: **Top Picks**, **Because you watched …** and **More Like This**,
plus a Netflix-style **match %** on the details page. Each JellyFlix profile is its own Jellyfin user, so every
profile gets its own recommendations.

## How it works

The engine (`Engine/RecommendationEngine.cs`) is content-based and has no dependency on Jellyfin, so it is unit tested
on its own.

1. **Titles become trait vectors.** Each movie and series is described by its genres, tags, studios, directors,
   writers, top six billed actors and decade. Every trait is weighted by its kind (a shared director says more than a
   shared decade, see `FeatureWeights`) and by how rare it is in the library (inverse document frequency), so sharing
   an unusual director counts for more than sharing "Drama".
2. **The profile's taste is learned from history** (`Library/TasteCollector.cs`):

   | Signal | Weight |
   | --- | --- |
   | Watched movie | 1.0 |
   | Movie in progress | 0.5 |
   | Series, by episodes watched | 0.5 → 1.0 at five episodes |
   | On My List (favorite) | +1.0 |
   | Thumbs up | +0.5 |
   | Thumbs down | −1.5 (replaces everything else) |

   Signals fade with age (half weight after 120 days, never below a quarter) so recent taste leads.
3. **Unwatched titles are scored** by cosine similarity to the taste (80%), community rating (12%) and how recently
   they were added (8%).
4. **Variety:** the list is re-ranked with maximal marginal relevance, so one franchise or genre can't fill a row.
5. **Because you watched** rows take the profile's most recent titles as seeds and rank titles by similarity to each;
   seeds with too few similar titles are skipped.
6. **Match %** ranks the title's fit against the whole library: the best fit shows 98%, the worst 60%.
7. **New profiles** with no history get well-rated, recently added titles (with a small daily shuffle).

Recommendations only ever include titles the profile is allowed to see, so parental controls and library access
apply. Each profile's model is cached and rebuilt when its watch history or the library changes, or after 30 minutes.

## Build and install

```sh
dotnet build src/JellyFlix.Recommendations -c Release
mkdir -p "<jellyfin data dir>/plugins/JellyFlix Recommendations_1.0.0.0"
cp src/JellyFlix.Recommendations/bin/Release/net10.0/JellyFlix.Recommendations.dll "<jellyfin data dir>/plugins/JellyFlix Recommendations_1.0.0.0/"
```

## API

All endpoints use the signed-in profile (not an API key).

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/JellyFlix/Recommendations?limit=20&rows=2&rowLength=20` | `{ IsPersonalized, Recommended: [{ Item, Reasons }], BecauseYouWatched: [{ SeedId, SeedName, Items }] }` |
| `GET` | `/JellyFlix/Recommendations/Similar/{itemId}?limit=12` | `{ Match, Items }` for a movie or series; 404 for anything else. |
