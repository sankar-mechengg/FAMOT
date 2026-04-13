% Define the target IP address and port number
targetIP = '127.0.0.1'; % Use the IP address of your Unity application
port = 8400; % The port number should match the one in your Unity UDPReceiver script

% Create a UDP object for sending data
u = udpport("datagram", "IPV4");

% Calibration signals
calibrationSignals = {'R', 'F', 'E', 'P', 'S'};
calibrationDuration = 3; % seconds

% Test parameters
numTargets = 10;
testDuration = 5; % seconds

% Rest position value
restValue = 50;

% Loop through each calibration signal
for signal = calibrationSignals
    % Send the calibration signal for the specified duration
    for t = 1:calibrationDuration*10 % 10 messages per second
        % Format the message as "C,signal"
        message = sprintf('C,%s', signal{1});
        
        % Convert the string message to uint8 data
        data = uint8(message);
        
        % Send the data to the target IP and port
        write(u, data, targetIP, port);
        
        % Pause for 0.1 seconds (10 messages per second)
        pause(0.1);
    end
end

% Send calibration N message with angle ranges and number of targets
% Format: C,N,outputMinFE,outputMaxFE,outputMinPS,outputMaxPS,numTargets
outputMinFE = -70;
outputMaxFE = 70;
outputMinPS = -20;
outputMaxPS = 90;
message = sprintf('C,N,%.2f,%.2f,%.2f,%.2f,%d', outputMinFE, outputMaxFE, outputMinPS, outputMaxPS, numTargets);
data = uint8(message);
write(u, data, targetIP, port);
pause(0.5);

% Loop through each target
for target = 1:numTargets
    % Randomly generate target values for t1 and t2
    t1_target = rand() * 100;
    t2_target = rand() * 100;
    
    % Initialize IMU values
    i1_value = restValue;
    i2_value = restValue;
    
    % Loop for the duration of the test
    for t = 1:testDuration*10 % 10 messages per second
        % Simulate the IMU values moving towards the target values
        i1_value = i1_value + (t1_target - i1_value) * 0.1; % Adjust the factor for faster/slower change
        i2_value = i2_value + (t2_target - i2_value) * 0.1;
        
        % Determine LED status: S if close to target, F otherwise
        fe_error = abs(i1_value - t1_target);
        ps_error = abs(i2_value - t2_target);
        if fe_error < 5 && ps_error < 5
            ledStatus = 'S'; % Success - close to target
        else
            ledStatus = 'F'; % Fail - not yet at target
        end
        
        % Format the message as "T,t1,t2,i1,i2,ledStatus"
        message = sprintf('T,%.2f,%.2f,%.2f,%.2f,%s', t1_target, t2_target, i1_value, i2_value, ledStatus);
        
        % Convert the string message to uint8 data
        data = uint8(message);
        
        % Send the data to the target IP and port
        write(u, data, targetIP, port);
        
        % Pause for 0.1 seconds (10 messages per second)
        pause(0.1);
    end
    
    % After each test, reset to the rest position
    restDuration = 3; % seconds for the rest position
    for t = 1:restDuration*10 % 10 messages per second
        % Format the message as "T,50,50,50,50,F"
        message = sprintf('T,%.2f,%.2f,%.2f,%.2f,F', restValue, restValue, restValue, restValue);
        
        % Convert the string message to uint8 data
        data = uint8(message);
        
        % Send the data to the target IP and port
        write(u, data, targetIP, port);
        
        % Pause for 0.1 seconds (10 messages per second)
        pause(0.1);
    end
end

% Cleanup
clear u;