% UDPReceiver_Stream.m - Receive the per-frame "D" stream that FAMOT sends out
%
% FAMOT streams one line per rendered frame to 127.0.0.1:8401 (configurable on
% the UdpStreamSender component). Column layout (0-based index in the README):
%   D, unity_time, frame, phase, trial, target,
%   live angles (7), target angles (7),
%   head px py pz qx qy qz qw,
%   gaze px py pz dx dy dz,
%   L valid px py pz qx qy qz qw,  R valid px py pz qx qy qz qw,
%   status (S|F|N)
%
% This script listens for 10 seconds and plots the live vs target wrist
% extension afterwards.

listenPort = 8401;
u = udpport("datagram", "IPV4", "LocalPort", listenPort);
u.Timeout = 1;

seconds = 10;
tStart = tic;
rows = {};
while toc(tStart) < seconds
    if u.NumDatagramsAvailable > 0
        d = read(u, u.NumDatagramsAvailable, "uint8");
        for i = 1:numel(d)
            line = char(d(i).Data);
            if startsWith(line, 'D,')
                rows{end+1} = line; %#ok<SAGROW>
            end
        end
    else
        pause(0.01);
    end
end
clear u;

fprintf('Received %d frames\n', numel(rows));
if isempty(rows), return; end

n = numel(rows);
t = zeros(n, 1); liveWrE = zeros(n, 1); tgtWrE = zeros(n, 1); status = cell(n, 1);
for i = 1:n
    f = strsplit(rows{i}, ',');
    t(i) = str2double(f{2});
    liveWrE(i) = str2double(f{7 + 5});   % live angles start at index 7 (1-based), wrist extension is the 6th
    tgtWrE(i) = str2double(f{14 + 5});   % target angles start at index 14
    status{i} = f{end};
end

figure; plot(t, liveWrE, 'b-', t, tgtWrE, 'r--');
xlabel('Unity time (s)'); ylabel('Wrist extension (deg)'); legend('live', 'target');
title('FAMOT stream: wrist extension');
