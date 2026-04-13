using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UDPReceiver : MonoBehaviour
{
    public int Port; // Port number to listen on
    private UdpClient _ReceiveClient; // Receiving UdpClient
    private Thread _ReceiveThread; // Thread to receive data

    // ConcurrentQueue to store the incoming messages
    private ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();

    // TextMeshPro to display the values
    public TMPro.TextMeshProUGUI infoText;
    public TMPro.TextMeshProUGUI headerText;

    // Hand GameObject and Transformations of Wrist and Elbow
    public GameObject orgHand;
    public GameObject ghostHand;

    public Transform wristTransformOrgHand;
    public Transform elbowTransformOrgHand;

    public Transform wristTransformGhostHand;
    public Transform elbowTransformGhostHand;

    public Image ledImage;

    private Vector3 wristLocalEulerAngles;
    private Vector3 elbowLocalEulerAngles;

    private Vector3 wristRestPositionGhostHand = new Vector3(0, 0, 0);
    private Vector3 elbowRestPositionGhostHand = new Vector3(0, 0, 0);
    private Vector3 wristRestPositionOrgHand = new Vector3(0, 0, 0);
    private Vector3 elbowRestPositionOrgHand = new Vector3(0, 0, 0);

    private float t1_value = 0;
    private float t2_value = 0;
    private float i1_value = 0;
    private float i2_value = 0;

    private float inputMin = 0;
    private float inputMax = 100;
    private float outputMinFE = -70;
    private float outputMaxFE = 70;
    private float outputMinPS = -20;
    private float outputMaxPS = 90;

    private int num_targets = 0;
    private int count_targets = 1;
    private int trialCount = 0;
    public TMP_Text infoTrialText;
    public TMP_Text infoTargetText;

    public MaximumAngleSO maximumAnglesSettings;

    //--------------------------------------------------------------------------------

    void Awake()
    {
        wristRestPositionGhostHand = wristTransformGhostHand.localEulerAngles;
        elbowRestPositionGhostHand = elbowTransformGhostHand.localEulerAngles;
        wristRestPositionOrgHand = wristTransformOrgHand.localEulerAngles;
        elbowRestPositionOrgHand = elbowTransformOrgHand.localEulerAngles;
    }

    //--------------------------------------------------------------------------------

    void Start()
    {
        // Load maximum angles from ScriptableObject
        if (maximumAnglesSettings != null)
        {
            outputMinFE = maximumAnglesSettings.maxAngleFlexion;
            outputMaxFE = maximumAnglesSettings.maxAngleExtension;
            outputMinPS = maximumAnglesSettings.maxAnglePronation;
            outputMaxPS = maximumAnglesSettings.maxAngleSupination;
        }

        Initialize();
    }

    //--------------------------------------------------------------------------------

    public void Initialize()
    {
        // Receive
        _ReceiveThread = new Thread(new ThreadStart(ReceiveData));
        _ReceiveThread.IsBackground = true;
        _ReceiveThread.Start();
    }

    //--------------------------------------------------------------------------------

    private void ReceiveData()
    {
        _ReceiveClient = new UdpClient(Port);

        while (true)
        {
            try
            {
                IPEndPoint anyIP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = _ReceiveClient.Receive(ref anyIP);
                string message = System.Text.Encoding.UTF8.GetString(data);

                if (!string.IsNullOrEmpty(message))
                {
                    // Enqueue the message for processing on the main thread
                    messageQueue.Enqueue(message);
                }
            }
            catch (Exception err)
            {
                Debug.Log(err.Message);
            }
        }
    }

    //--------------------------------------------------------------------------------

    void Update()
    {
        // Process all messages in the queue
        while (messageQueue.TryDequeue(out string message))
        {
            ProcessMessage(message);
        }
    }

    //--------------------------------------------------------------------------------

    private void ProcessMessage(string message) //(C,R), (C,F), (C,E), (C,P), (C,S), (C,N,n1,n2,n3,n4), (T,t1,t2,i1,i2)
    {
        string[] splitMessage = message.Split(','); //[C][R], [C][F], [C][E], [C][P], [C][S], [C][N], [T][t1][t2][i1][i2]

        if (splitMessage.Length < 2)
            return;

        if (splitMessage[0] == "C")
        {
            headerText.text = "Calibration Mode";
            ghostHand.SetActive(false);

            switch (splitMessage[1])
            {
                case "R":
                    infoText.text = "Move your Hand to Rest Position";
                    wristTransformOrgHand.localEulerAngles = wristRestPositionOrgHand;
                    elbowTransformOrgHand.localEulerAngles = elbowRestPositionOrgHand;
                    wristTransformGhostHand.localEulerAngles = wristRestPositionGhostHand;
                    elbowTransformGhostHand.localEulerAngles = elbowRestPositionGhostHand;
                    break;
                case "F":
                    infoText.text = "Move your Hand to Maximum Flexion Position";

                    wristTransformOrgHand.localEulerAngles = wristRestPositionOrgHand;
                    elbowTransformOrgHand.localEulerAngles = elbowRestPositionOrgHand;

                    // Create a temporary Vector3 to modify the localEulerAngles
                    Vector3 tempEulerAngles = wristRestPositionOrgHand;
                    tempEulerAngles.x = outputMinFE;

                    // Assign the modified Vector3 back to localEulerAngles
                    wristTransformOrgHand.localEulerAngles = tempEulerAngles;

                    break;
                case "E":
                    infoText.text = "Move your Hand to Maximum Extension Position";

                    wristTransformOrgHand.localEulerAngles = wristRestPositionOrgHand;
                    elbowTransformOrgHand.localEulerAngles = elbowRestPositionOrgHand;

                    // Create a temporary Vector3 to modify the localEulerAngles
                    tempEulerAngles = wristRestPositionOrgHand;
                    tempEulerAngles.x = outputMaxFE;

                    // Assign the modified Vector3 back to localEulerAngles
                    wristTransformOrgHand.localEulerAngles = tempEulerAngles;

                    break;
                case "P":
                    infoText.text = "Move your Hand to Maximum Pronation Position";

                    wristTransformOrgHand.localEulerAngles = wristRestPositionOrgHand;
                    elbowTransformOrgHand.localEulerAngles = elbowRestPositionOrgHand;

                    tempEulerAngles = elbowRestPositionOrgHand;
                    tempEulerAngles.y = -(outputMinPS);

                    elbowTransformOrgHand.localEulerAngles = tempEulerAngles;
                    break;
                case "S":
                    infoText.text = "Move your Hand to Maximum Supination Position";

                    wristTransformOrgHand.localEulerAngles = wristRestPositionOrgHand;
                    elbowTransformOrgHand.localEulerAngles = elbowRestPositionOrgHand;

                    tempEulerAngles = elbowRestPositionOrgHand;
                    tempEulerAngles.y = -(outputMaxPS);

                    elbowTransformOrgHand.localEulerAngles = tempEulerAngles;

                    break;
                case "N":
                    if (splitMessage.Length < 7)
                    {
                        Debug.LogWarning("C,N message requires 7 fields but received " + splitMessage.Length);
                        break;
                    }
                    string n1_msg = splitMessage[2];
                    string n2_msg = splitMessage[3];
                    string n3_msg = splitMessage[4];
                    string n4_msg = splitMessage[5];
                    string n_tar = splitMessage[6];

                    outputMinFE = float.Parse(n1_msg);
                    outputMaxFE = float.Parse(n2_msg);
                    outputMinPS = float.Parse(n3_msg);
                    outputMaxPS = float.Parse(n4_msg);
                    num_targets = (int)(float.Parse(n_tar));

                    infoTrialText.text = "Trial 1";

                    break;
            }
        }
        else if (splitMessage[0] == "T")
        {
            headerText.text = "Test Mode";
            infoText.text =
                "Achieve the Target (Ghost Hand) by Moving your Hand and Matching it with the Ghost Hand as close as possible";

            if (!ghostHand.activeSelf)
                ghostHand.SetActive(true);

            //Store previous values of t1
            float prev_t1_value = t1_value;

            //t1, t2, i1, i2 correspond to FE Target, PS Target, FE Input, PS Input
            string t1_msg = splitMessage[1];
            string t2_msg = splitMessage[2];
            string i1_msg = splitMessage[3];
            string i2_msg = splitMessage[4];

            t1_value = float.Parse(t1_msg);
            t2_value = float.Parse(t2_msg);
            i1_value = float.Parse(i1_msg);
            i2_value = float.Parse(i2_msg);

            //Clamp the values between 0 and 100
            // t1_value = Mathf.Clamp(t1_value, 0, 100);
            // t2_value = Mathf.Clamp(t2_value, 0, 100);
            // i1_value = Mathf.Clamp(i1_value, 0, 100);
            // i2_value = Mathf.Clamp(i2_value, 0, 100);

            // t1_value = !string.IsNullOrEmpty(t1_msg) ? float.Parse(t1_msg) : t1_value;
            // t2_value = !string.IsNullOrEmpty(t2_msg) ? float.Parse(t2_msg) : t2_value;
            // i1_value = !string.IsNullOrEmpty(i1_msg) ? float.Parse(i1_msg) : i1_value;
            // i2_value = !string.IsNullOrEmpty(i2_msg) ? float.Parse(i2_msg) : i2_value;

            // Piecewise mapping: 0-50 maps to min-to-0, 50-100 maps to 0-to-max
            // This ensures input=50 always maps to 0° (rest position)
            t1_value = PiecewiseMap(t1_value, outputMinFE, outputMaxFE);
            t2_value = PiecewiseMap(t2_value, outputMinPS, outputMaxPS);
            i1_value = PiecewiseMap(i1_value, outputMinFE, outputMaxFE);
            i2_value = PiecewiseMap(i2_value, outputMinPS, outputMaxPS);

            //Change the orientation of the wrist and elbow of the ghost hand and original hand
            wristLocalEulerAngles = wristTransformGhostHand.localEulerAngles;
            wristLocalEulerAngles.x = t1_value;
            wristTransformGhostHand.localEulerAngles = wristLocalEulerAngles;

            elbowLocalEulerAngles = elbowTransformGhostHand.localEulerAngles;
            elbowLocalEulerAngles.y = -(t2_value);
            elbowTransformGhostHand.localEulerAngles = elbowLocalEulerAngles;

            wristLocalEulerAngles = wristTransformOrgHand.localEulerAngles;
            wristLocalEulerAngles.x = i1_value;
            wristTransformOrgHand.localEulerAngles = wristLocalEulerAngles;

            elbowLocalEulerAngles = elbowTransformOrgHand.localEulerAngles;
            elbowLocalEulerAngles.y = -(i2_value);
            elbowTransformOrgHand.localEulerAngles = elbowLocalEulerAngles;

            // if (t1_value == 0)
            // {
            //     wristTransformGhostHand.localEulerAngles = wristRestPositionGhostHand;
            //     wristTransformOrgHand.localEulerAngles = wristRestPositionOrgHand;
            // }
            // if (t2_value == 0)
            // {
            //     elbowTransformGhostHand.localEulerAngles = elbowRestPositionGhostHand;
            //     elbowTransformOrgHand.localEulerAngles = elbowRestPositionOrgHand;
            // }


            //Set the LED color based on the change in t1 and t2 values
            if (splitMessage.Length > 5)
            {
                if (splitMessage[5] == "S")
                {
                    ledImage.color = Color.green;
                }
                else if (splitMessage[5] == "F")
                {
                    Color greyColor = new Color(0.71f, 0.71f, 0.71f, 1);
                    //change colour to gray
                    ledImage.color = greyColor;
                }
            }

            //Keep counting the number of targets achieved
            if (t1_value != prev_t1_value)
            {
                count_targets++;
                int tar_num = num_targets - count_targets;
                //Display the number of targets left
                infoTargetText.text = "Target " + tar_num.ToString();
            }

            //Display the number of targets achieved
            if (count_targets == num_targets)
            {
                trialCount++;
                count_targets = 1;
                infoTrialText.text = "Trial " + trialCount.ToString();
            }
        }
    }

    //--------------------------------------------------------------------------------

    /// <summary>
    /// Piecewise linear mapping: input 0-50 maps to outputMin-to-0, input 50-100 maps to 0-to-outputMax.
    /// This ensures input=50 always maps to 0° (rest position) regardless of asymmetric ranges.
    /// </summary>
    private float PiecewiseMap(float input, float outputMin, float outputMax)
    {
        if (input <= 50f)
            return Mathf.Lerp(outputMin, 0f, input / 50f);
        else
            return Mathf.Lerp(0f, outputMax, (input - 50f) / 50f);
    }

    //--------------------------------------------------------------------------------
    private void OnApplicationQuit()
    {
        try
        {
            if (_ReceiveThread != null && _ReceiveThread.IsAlive)
            {
                _ReceiveThread.Abort();
                _ReceiveThread = null;
            }
            _ReceiveClient?.Close();
        }
        catch (Exception err)
        {
            Debug.Log(err.Message);
        }
    }
}
