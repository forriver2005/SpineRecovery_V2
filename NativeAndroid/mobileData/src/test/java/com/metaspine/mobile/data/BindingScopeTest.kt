package com.metaspine.mobile.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * P2.1 / P2.7 绑定作用域：凭据、队列与在途回包只有在同一个 scope 下才互相认可。
 */
class BindingScopeTest {

    private fun scope(
        origin: String = "https://hospital-a.example.com",
        serviceId: String = "svc-a",
        hospitalKey: String = "hospital_a",
        patientId: String = "p_1001",
        accountId: String = "account_1",
        generation: Long = 1L
    ) = BindingScope(origin, serviceId, hospitalKey, "医院A", patientId, accountId, generation)

    @Test
    fun `完全相同的作用域互相匹配`() {
        assertTrue(scope().matches(scope()))
    }

    @Test
    fun `空作用域永不匹配`() {
        assertFalse(scope().matches(null))
    }

    @Test
    fun `换医院即换作用域`() {
        assertFalse(scope().matches(scope(origin = "https://hospital-b.example.com")))
        assertFalse(scope().matches(scope(serviceId = "svc-b")))
        assertFalse(scope().matches(scope(hospitalKey = "hospital_b")))
    }

    @Test
    fun `同医院换患者也不算同一绑定`() {
        assertFalse(scope().matches(scope(patientId = "p_2002")))
        assertFalse(scope().matches(scope(accountId = "account_2")))
    }

    @Test
    fun `重新激活推进代际使旧回包失效`() {
        assertFalse("同内容但代际不同", scope().matches(scope(generation = 2L)))
    }

    @Test
    fun `医院名称变化不影响身份判定`() {
        // hospitalName 只是展示字段，不参与 scope 匹配。
        val renamed = scope().copy(hospitalName = "医院A（新区）")
        assertTrue(scope().matches(renamed))
    }

    @Test
    fun `缺省代际在比较时按 0 处理`() {
        assertEquals(0L, BindingScope("o", "s", "h", "n", "p", "a", 0L).credentialGeneration)
        assertNotEquals(scope(generation = 0L), scope(generation = 1L))
    }
}
