package com.metaspine.mobile.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

class VerifiedAccountStatusTest {
    private class Effects {
        var verified = 0
        var removed = 0

        fun apply(
            status: PatientAccountStatus,
            bindingVerified: Boolean,
            isCurrent: () -> Boolean = { true }
        ): AccountLoginResult = applyVerifiedAccountStatus(
            status = status,
            bindingVerified = bindingVerified,
            isCurrent = isCurrent,
            markVerified = { verified++ },
            removeAccount = { removed++ }
        )
    }

    @Test
    fun `active current response marks either binding verified without removing data`() {
        for (previouslyVerified in listOf(false, true)) {
            val effects = Effects()
            val result = effects.apply(PatientAccountStatus.Active, previouslyVerified)

            assertSame(AccountLoginResult.Allowed, result)
            assertEquals(1, effects.verified)
            assertEquals(0, effects.removed)
        }
    }

    @Test
    fun `missing on an unverified legacy binding preserves the account`() {
        val effects = Effects()
        val result = effects.apply(PatientAccountStatus.Missing, bindingVerified = false)

        assertTrue(result is AccountLoginResult.Blocked)
        assertEquals(0, effects.verified)
        assertEquals(0, effects.removed)
    }

    @Test
    fun `explicit missing for a verified current binding removes once`() {
        val effects = Effects()
        val result = effects.apply(PatientAccountStatus.Missing, bindingVerified = true)

        assertTrue(result is AccountLoginResult.Blocked)
        assertEquals(AccountLoginGuard.DELETED_MESSAGE, (result as AccountLoginResult.Blocked).message)
        assertEquals(0, effects.verified)
        assertEquals(1, effects.removed)
    }

    @Test
    fun `rejected and unavailable responses never verify or remove either binding`() {
        for (status in listOf(PatientAccountStatus.Rejected, PatientAccountStatus.Unavailable)) {
            for (previouslyVerified in listOf(false, true)) {
                val effects = Effects()
                val result = effects.apply(status, previouslyVerified)

                assertTrue("$status must remain blocked", result is AccountLoginResult.Blocked)
                assertEquals("$status must not verify a binding", 0, effects.verified)
                assertEquals("$status must preserve the account", 0, effects.removed)
            }
        }
    }

    @Test
    fun `every stale response is blocked without verification or deletion`() {
        for (status in PatientAccountStatus.values()) {
            for (previouslyVerified in listOf(false, true)) {
                val effects = Effects()
                val result = effects.apply(status, previouslyVerified) { false }

                assertTrue("Stale $status must remain blocked", result is AccountLoginResult.Blocked)
                assertEquals("Stale $status must not verify a binding", 0, effects.verified)
                assertEquals("Stale $status must preserve the account", 0, effects.removed)
            }
        }
    }

    @Test
    fun `network epoch changing before a missing response is applied preserves verified account`() {
        var currentEpoch = 7L
        val requestEpoch = currentEpoch
        val isCurrent = { currentEpoch == requestEpoch }
        val effects = Effects()

        currentEpoch++
        val result = effects.apply(PatientAccountStatus.Missing, bindingVerified = true, isCurrent = isCurrent)

        assertTrue(result is AccountLoginResult.Blocked)
        assertEquals(0, effects.verified)
        assertEquals(0, effects.removed)
    }
}
