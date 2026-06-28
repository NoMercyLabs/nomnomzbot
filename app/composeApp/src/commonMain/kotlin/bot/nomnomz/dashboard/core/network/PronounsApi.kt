package bot.nomnomz.dashboard.core.network

/** REST interface for `/api/v1/pronouns/` — pronoun catalog + viewer self-service. */
interface PronounsApi {
    /** Anonymous catalog from the dedicated endpoint (includes `key` field for alejo matching). */
    suspend fun catalog(): ApiResult<List<PronounOption>>

    /** The authenticated viewer's current pronoun state. */
    suspend fun getMyPronouns(): ApiResult<UserPronounResponse>

    /** Set or clear the authenticated viewer's pronouns. */
    suspend fun setMyPronouns(body: SetPronounBody): ApiResult<UserPronounResponse>
}

internal class PronounsApiImpl(private val client: ApiClient) : PronounsApi {
    override suspend fun catalog(): ApiResult<List<PronounOption>> =
        client.getEnvelope("api/v1/pronouns/catalog")

    override suspend fun getMyPronouns(): ApiResult<UserPronounResponse> =
        client.getEnvelope("api/v1/pronouns/me")

    override suspend fun setMyPronouns(body: SetPronounBody): ApiResult<UserPronounResponse> =
        client.putEnvelope("api/v1/pronouns/me", body)
}
