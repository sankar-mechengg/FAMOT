% UDPSender_7DOF.m - Example sender for the FAMOT 7-DOF protocol (K / E messages)
%
% Drives the ghost (target) arm and, when the live arm input source is "UDP",
% the live arm itself with full upper-limb angles. Angles are degrees relative
% to the calibrated rest pose, in this order:
%   1 shoulder flexion (+ forward)       5 forearm supination (+ palm up)
%   2 shoulder abduction (+ lateral)     6 wrist extension   (+ dorsiflexion)
%   3 shoulder rotation (+ external)     7 wrist radial dev. (+ towards thumb)
%   4 elbow flexion (+ flexion)
%
% Message formats (see README "UDP Communication Protocol"):
%   K,t1..t7,i1..i7[,S|F]  targets + inputs (+ optional success flag)
%   E,NAME[,value[,detail]] event marker (TRIAL_START, TARGET_START, MARK, ...)
%   Q                        query -> FAMOT replies with S,<version>,<rec>,<session>,<port>

targetIP = '127.0.0.1';
port = 8400;
u = udpport("datagram", "IPV4");
send = @(msg) write(u, uint8(msg), targetIP, port);

numTrials = 2;
targetsPerTrial = 5;
holdSeconds = 3;      % how long each target is shown
restSeconds = 2;

% Joint ranges used to draw random targets (relative to rest, degrees)
jointMin = [-20 -10 -30 0 -20 -60 -15];
jointMax = [ 60  40  30 90 90  60  15];

send('Q');
for trial = 1:numTrials
    send(sprintf('E,TRIAL_START,%d', trial));
    for t = 1:targetsPerTrial
        target = jointMin + rand(1, 7) .* (jointMax - jointMin);
        send(sprintf('E,TARGET_START,%d', t));
        input = zeros(1, 7);
        for k = 1:holdSeconds * 20            % 20 Hz
            % Simulated subject converging on the target
            input = input + (target - input) * 0.15;
            err = max(abs(input - target));
            flag = 'F'; if err < 5, flag = 'S'; end
            msg = sprintf('K,%s,%s,%s', ...
                strjoin(compose('%.2f', target), ','), ...
                strjoin(compose('%.2f', input), ','), flag);
            send(msg);
            pause(0.05);
        end
        send(sprintf('E,TARGET_END,%d', t));
        send('E,REST_START');
        pause(restSeconds);
        send('E,REST_END');
    end
    send(sprintf('E,TRIAL_END,%d', trial));
end
send('E,MARK,0,protocol finished');
clear u;
