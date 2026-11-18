package com.metaspine.training;

import android.os.SystemClock;
import dev.slimevr.VRServer;
import dev.slimevr.tracking.processor.Bone;
import dev.slimevr.tracking.processor.BoneType;
import dev.slimevr.tracking.processor.HumanPoseManager;
import dev.slimevr.tracking.trackers.Tracker;
import io.github.axisangles.ktmath.Vector3;
import java.util.HashSet;
import java.util.Set;

/** One user-initiated attempt. Sample the live skeleton and execute both resets on its owner thread. */
public final class BetweenActionCalibration {
    private final VRServer server = VRServer.Companion.getInstance();
    private final Set<Tracker> participants = new HashSet<>();
    private volatile int phase = 1; // standing, skiing, complete, or -1 on failure
    private volatile int remaining = 3;
    private boolean pending, closed;
    private long lastSample, stableSince;
    private final double[] anchor = new double[3];

    public int phase() { return phase; }
    public int remaining() { return remaining; }
    public synchronized void close() { closed = true; }

    public synchronized void poll() {
        if (closed || pending || phase < 1 || phase > 2) return;
        pending = true;
        server.queueTask(() -> {
            synchronized (this) {
                try {
                    if (!closed) sample();
                } catch (RuntimeException ex) {
                    android.util.Log.e("BetweenActionCalibration", "Calibration failed", ex);
                    phase = -1;
                } finally {
                    pending = false;
                }
            }
        });
    }

    private void sample() {
        Set<Tracker> assigned = new HashSet<>();
        for (Tracker t : server.getAllTrackers()) {
            if (t.isInternal() || !t.isImu() || t.getTrackerPosition() == null) continue;
            if (!t.getStatus().getSendData() || (phase == 2 && t.getNeedReset())) {
                phase = -1;
                return;
            }
            if (t.getAllowMounting()) assigned.add(t);
        }
        if (assigned.isEmpty() || (!participants.isEmpty() && !participants.equals(assigned))) {
            phase = -1;
            return;
        }
        participants.addAll(assigned);
        HumanPoseManager pose = server.humanPoseManager;
        double[] angles = { tilt(pose, BoneType.CHEST), tilt(pose, BoneType.LEFT_UPPER_LEG),
                            tilt(pose, BoneType.RIGHT_UPPER_LEG) };
        boolean ready = phase == 1
            ? angles[0] < 30 && angles[1] < 30 && angles[2] < 30
            : angles[0] > 12 && angles[0] < 75 && angles[1] > 15 && angles[1] < 85
                && angles[2] > 15 && angles[2] < 85;
        long now = SystemClock.elapsedRealtime();
        if (!ready || now - lastSample > 600) stableSince = 0;
        lastSample = now;
        if (!ready) { remaining = 3; return; }
        if (stableSince == 0 || Math.abs(angles[0]-anchor[0]) > 6 ||
            Math.abs(angles[1]-anchor[1]) > 6 || Math.abs(angles[2]-anchor[2]) > 6) {
            stableSince = now;
            System.arraycopy(angles, 0, anchor, 0, angles.length);
        }
        long elapsed = now - stableSince;
        remaining = (int)Math.max(0, 3 - elapsed / 1000);
        if (elapsed < 3000) return;
        if (phase == 1) pose.resetTrackersFull("MetaSpine");
        else pose.resetTrackersMounting("MetaSpine");
        stableSince = 0;
        remaining = 3;
        phase++;
    }

    private static double tilt(HumanPoseManager pose, BoneType type) {
        Bone bone = pose.getBone(type);
        Vector3 a = bone.getPosition(), b = bone.getTailPosition();
        return Math.toDegrees(Math.atan2(Math.hypot(a.getX()-b.getX(), a.getZ()-b.getZ()),
                a.getY()-b.getY()));
    }
}
