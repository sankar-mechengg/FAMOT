% UDPSender_Test.m - Temporary script to experiment with mapping behavior
% This script sends specific known values so you can observe what each
% input value maps to in Unity.

targetIP = '127.0.0.1';
port = 8400;
u = udpport("datagram", "IPV4");

% First send calibration N with your current angle ranges
% Format: C,N, outputMinFE, outputMaxFE, outputMinPS, outputMaxPS, numTargets
outputMinFE = -70;   % Flexion limit (mapped from input 0)
outputMaxFE = 70;    % Extension limit (mapped from input 100)
outputMinPS = -20;   % Pronation limit (mapped from input 0)
outputMaxPS = 90;    % Supination limit (mapped from input 100)

% --- PIECEWISE MAPPING (same as in UDPReceiver.cs) ---
% Rest is ALWAYS at input=50 -> 0 degrees
%
% If input <= 50: mapped = outputMin * (1 - input/50)
%   input=0   -> outputMin (Max Flexion / Max Pronation)
%   input=50  -> 0 deg (Rest)
%
% If input > 50:  mapped = outputMax * (input-50)/50
%   input=50  -> 0 deg (Rest)
%   input=100 -> outputMax (Max Extension / Max Supination)
%
% For FE (outputMinFE=-60, outputMaxFE=60):
%   input=0   -> -60 deg (Max Flexion)
%   input=50  ->   0 deg (Rest)
%   input=100 ->  60 deg (Max Extension)
%
% For PS (outputMinPS=-20, outputMaxPS=90):
%   input=0   -> -20 deg (Max Pronation)
%   input=50  ->   0 deg (Rest)
%   input=100 ->  90 deg (Max Supination)
% Asymmetry in PS is handled by MATLAB (IMU normalization)
% Unity simply maps 0-50 to min-to-0, 50-100 to 0-to-max
% -----------------------------------------------

fprintf('=== Mapping Table (Piecewise) ===\n');
fprintf('%-10s | %-15s | %-15s\n', 'Input', 'FE Angle', 'PS Angle');
fprintf('---------- | --------------- | ---------------\n');
for input = 0:10:100
    fe_angle = piecewise_map(input, outputMinFE, outputMaxFE);
    ps_angle = piecewise_map(input, outputMinPS, outputMaxPS);
    fprintf('%-10d | %-15.2f | %-15.2f\n', input, fe_angle, ps_angle);
end
fprintf('\n');

% Send calibration N
message = sprintf('C,N,%.2f,%.2f,%.2f,%.2f,%d', outputMinFE, outputMaxFE, outputMinPS, outputMaxPS, 100);
data = uint8(message);
write(u, data, targetIP, port);
pause(1);

% =====================================================================
% TEST 1: Sweep FE from 0 to 100 while keeping PS at 50 (rest)
% This lets you see pure Flexion -> Rest -> Extension
% =====================================================================
fprintf('TEST 1: Sweeping FE (0->100), PS fixed at 50\n');
fprintf('Watch the wrist rotate (x-axis). Should go from Flexion to Extension.\n');
for fe_input = 0:2:100
    ps_input = 50; % PS at rest
    message = sprintf('T,%.2f,%.2f,%.2f,%.2f,F', fe_input, ps_input, fe_input, ps_input);
    data = uint8(message);
    write(u, data, targetIP, port);
    pause(0.05);
end
pause(2);

% =====================================================================
% TEST 2: Sweep PS from 0 to 100 while keeping FE at 50 (rest)
% This lets you see pure Pronation -> Rest -> Supination
% =====================================================================
fprintf('TEST 2: Sweeping PS (0->100), FE fixed at 50\n');
fprintf('Watch the elbow rotate (y-axis). Should go from Pronation to Supination.\n');
for ps_input = 0:2:100
    fe_input = 50; % FE at rest
    message = sprintf('T,%.2f,%.2f,%.2f,%.2f,F', fe_input, ps_input, fe_input, ps_input);
    data = uint8(message);
    write(u, data, targetIP, port);
    pause(0.05);
end
pause(2);

% =====================================================================
% TEST 3: Hold specific positions to verify mapping
% Each position is held for 3 seconds so you can observe
% =====================================================================
testPositions = [
    0,   50;   % Max Flexion, PS rest
    50,  50;   % Rest, Rest
    100, 50;   % Max Extension, PS rest
    50,  0;    % FE rest, Max Pronation
    50,  50;   % Rest, Rest
    50,  100;  % FE rest, Max Supination
    0,   0;    % Max Flexion + Max Pronation
    100, 100;  % Max Extension + Max Supination
    50,  50;   % Rest, Rest
];

testLabels = {
    'Max Flexion (FE=0, PS=50)',
    'Rest (FE=50, PS=50)',
    'Max Extension (FE=100, PS=50)',
    'Max Pronation (FE=50, PS=0)',
    'Rest (FE=50, PS=50)',
    'Max Supination (FE=50, PS=100)',
    'Max Flexion + Max Pronation (FE=0, PS=0)',
    'Max Extension + Max Supination (FE=100, PS=100)',
    'Rest (FE=50, PS=50)'
};

fprintf('\nTEST 3: Holding specific positions (3 sec each)\n');
for i = 1:size(testPositions, 1)
    fe_input = testPositions(i, 1);
    ps_input = testPositions(i, 2);
    
    fe_angle = piecewise_map(fe_input, outputMinFE, outputMaxFE);
    ps_angle = piecewise_map(ps_input, outputMinPS, outputMaxPS);
    
    fprintf('Position %d: %s -> FE=%.1f deg, PS=%.1f deg\n', i, testLabels{i}, fe_angle, ps_angle);
    
    for t = 1:30  % 3 seconds at 10 msgs/sec
        % Target and input are same (no ghost hand mismatch)
        message = sprintf('T,%.2f,%.2f,%.2f,%.2f,S', fe_input, ps_input, fe_input, ps_input);
        data = uint8(message);
        write(u, data, targetIP, port);
        pause(0.1);
    end
end

fprintf('\nDone! All tests complete.\n');

% Cleanup
clear u;

% Piecewise mapping function (matches UDPReceiver.cs PiecewiseMap)
function mapped = piecewise_map(input, outputMin, outputMax)
    if input <= 50
        mapped = outputMin * (1 - input / 50);
    else
        mapped = outputMax * (input - 50) / 50;
    end
end
