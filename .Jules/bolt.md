## 2024-05-22 - Session Payload Caching
**Learning:** The `SessionEntryEnvelope` uses `JsonElement` for `Payload` to allow flexible content types, but this causes repeated deserialization when accessing history (e.g., for context reconstruction or compaction).
**Action:** Implemented `CachedPayload` in `SessionEntryEnvelope` to cache the deserialized object. Future envelope-like structures should consider similar caching mechanisms if read frequently.
