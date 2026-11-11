package com.metaspine.mobile.data

data class DemoAccount(
    val accountId: String,
    val password: String,
    val initialLifecycle: UserLifecycle
)

object DemoAccountDatabase {
    private val accounts: Map<String, DemoAccount> = buildList {
        for (index in 1..10) {
            add(
                DemoAccount(
                    accountId = "new$index",
                    password = DEFAULT_PASSWORD,
                    initialLifecycle = UserLifecycle.AdmissionPending
                )
            )
            add(
                DemoAccount(
                    accountId = "old$index",
                    password = DEFAULT_PASSWORD,
                    initialLifecycle = UserLifecycle.ReturningUser
                )
            )
        }
    }.associateBy { it.accountId }

    fun authenticate(accountId: String, password: String): DemoAccount? {
        val normalizedId = accountId.trim().lowercase()
        return accounts[normalizedId]?.takeIf { it.password == password }
    }

    fun find(accountId: String): DemoAccount? = accounts[accountId.trim().lowercase()]

    fun newAccountIds(): Set<String> = accounts.values
        .filter { it.initialLifecycle == UserLifecycle.AdmissionPending }
        .mapTo(linkedSetOf()) { it.accountId }

    fun accountHint(): String = "new1-new10 / old1-old10"

    private const val DEFAULT_PASSWORD = "123"
}
